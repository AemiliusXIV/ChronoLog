// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.DutyState;
using ChronoLog.Model;
using ChronoLog.Phases;

namespace ChronoLog.Capture;

/// <summary>
/// Drives the pull lifecycle off IDutyState (start / wipe / recommence / complete) and
/// per-tick state (combat edge, boss HP, phase, first death). Resolves each pull, applies
/// the short-pull filter, and raises events the output sinks subscribe to.
/// </summary>
public sealed class DutyTracker : IDisposable
{
    private readonly Configuration config;
    private readonly BossHpReader boss;
    private readonly PhaseResolver phases;
    private readonly SessionStore store;
    private readonly Func<TimeSpan?> getRecordOffset;
    private readonly Func<ulong, string?> getDeathCause;

    private RaidSession? session;
    private bool combatStartSeen;

    public RaidSession? Session => session;
    public BossHpReader Boss => boss;

    public event Action<PullEntry>? PullStarted;

    /// <summary>
    /// Fires the first tick combat is detected for a pull, after RecordOffsetAtStart is set.
    /// Live OBS chapter markers hang off this rather than PullStarted so the marker lands at
    /// the actual pull, not at zone-in/recommence which can be a minute or two earlier.
    /// </summary>
    public event Action<PullEntry>? CombatStarted;

    public event Action<PullEntry>? PullCommitted;
    public event Action<PullEntry>? PullDiscarded;
    public event Action<RaidSession>? Cleared;
    public event Action<RaidSession>? SessionEnded;

    public DutyTracker(
        Configuration config,
        BossHpReader boss,
        PhaseResolver phases,
        SessionStore store,
        Func<TimeSpan?> getRecordOffset,
        Func<ulong, string?> getDeathCause)
    {
        this.config = config;
        this.boss = boss;
        this.phases = phases;
        this.store = store;
        this.getRecordOffset = getRecordOffset;
        this.getDeathCause = getDeathCause;

        Plugin.DutyState.DutyStarted += OnDutyStarted;
        Plugin.DutyState.DutyWiped += OnDutyWiped;
        Plugin.DutyState.DutyRecommenced += OnDutyRecommenced;
        Plugin.DutyState.DutyCompleted += OnDutyCompleted;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        Plugin.DutyState.DutyStarted -= OnDutyStarted;
        Plugin.DutyState.DutyWiped -= OnDutyWiped;
        Plugin.DutyState.DutyRecommenced -= OnDutyRecommenced;
        Plugin.DutyState.DutyCompleted -= OnDutyCompleted;
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;
    }

    /// <summary>Feeds an observed ability into phase detection for the active pull.</summary>
    public void NoteBossAction(uint actionId)
    {
        if (session?.Current == null)
            return;

        var newPhase = phases.NoteAction(session.TerritoryId, actionId);
        if (newPhase != null && config.PhaseTimestampsEnabled && session.Current.PhaseLog.Count > 0)
        {
            session.Current.PhaseLog.Add(new PhaseTransitionEntry
            {
                PhaseName = newPhase,
                RecordOffset = getRecordOffset(),
                Utc = DateTime.UtcNow,
            });
        }
    }

    public void Tick()
    {
        if (session?.Current == null)
            return;

        boss.Tick();
        phases.Tick(boss);

        if (!combatStartSeen && Plugin.Condition[ConditionFlag.InCombat])
        {
            combatStartSeen = true;
            session.Current.CombatStartUtc = DateTime.UtcNow;

            // Capture the OBS offset now - at actual combat start - not at zone-in or
            // recommence time, which can be 30s-2min before the pull actually begins.
            session.Current.RecordOffsetAtStart = getRecordOffset();

            // Back-fill the P1 phase log entry; it was added in BeginPull without an
            // offset because we didn't have one yet.
            if (session.Current.PhaseLog.Count > 0)
                session.Current.PhaseLog[0].RecordOffset = session.Current.RecordOffsetAtStart;

            CombatStarted?.Invoke(session.Current);
        }

        if (combatStartSeen && session.Current.FirstDeath == null)
            DetectFirstDeath();
    }

    // â”€â”€ Lifecycle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void OnDutyStarted(IDutyStateEventArgs args)
    {
        StartSessionIfNeeded(Plugin.ClientState.TerritoryType);
        BeginPull();
    }

    private void OnDutyRecommenced(IDutyStateEventArgs args)
    {
        StartSessionIfNeeded(Plugin.ClientState.TerritoryType);
        BeginPull();
    }

    private void OnDutyWiped(IDutyStateEventArgs args) => ResolvePull(PullOutcome.Wipe);

    private void OnDutyCompleted(IDutyStateEventArgs args) => ResolvePull(PullOutcome.Clear);

    private void OnTerritoryChanged(uint territory)
    {
        // Left the instance: finalise whatever session we had.
        if (session != null && session.TerritoryId != territory)
            EndSession();
    }

    private void StartSessionIfNeeded(uint territory)
    {
        if (session != null && session.TerritoryId == territory)
            return;

        if (session != null)
            EndSession();

        // Resume a recent, non-cleared run of the same fight so prog accumulates across
        // resets and reloads; otherwise start a fresh one and add it to the store.
        var resumed = config.ResumeSessionAcrossRestarts ? store.FindResumable(territory) : null;
        if (resumed != null)
        {
            session = resumed;
            session.LastActiveUtc = DateTime.UtcNow;
        }
        else
        {
            session = new RaidSession
            {
                TerritoryId = territory,
                FightName = Plugin.GetDutyName(territory) ?? $"Territory {territory}",
            };
            store.Add(session);
        }
    }

    private void BeginPull()
    {
        if (session == null || session.Current != null)
            return;

        var pull = new PullEntry
        {
            Attempt = session.AttemptCounter + 1,
            FightName = session.FightName,
            StartUtc = DateTime.UtcNow,
            // RecordOffsetAtStart is intentionally not set here. Zone-in and DutyRecommenced
            // both fire well before combat begins, so the offset would be wrong. It is captured
            // in Tick() the first frame InCombat becomes true.
        };

        boss.Reset();
        phases.BeginPull(session.TerritoryId);
        combatStartSeen = false;

        if (config.PhaseTimestampsEnabled)
        {
            pull.PhaseLog.Add(new PhaseTransitionEntry
            {
                PhaseName = phases.CurrentPhase,
                RecordOffset = pull.RecordOffsetAtStart,
                Utc = pull.StartUtc,
            });
        }

        session.Current = pull;
        PullStarted?.Invoke(pull);
    }

    private void ResolvePull(PullOutcome outcome)
    {
        if (session?.Current == null)
            return;

        var pull = session.Current;
        session.Current = null;

        pull.EndUtc = DateTime.UtcNow;
        pull.Outcome = outcome;
        pull.EndPhase = phases.CurrentPhase;

        // HP at the end: a clear means the boss is dead (always 0%).
        // For wipes, use the lowest HP seen - but fall back to LastNonZeroHpFraction when
        // LowestHpFraction is 0. That 0 reading comes from enrage sequences or phase
        // transitions where the boss is scripted to 0 HP while still briefly targetable,
        // not from the players' damage actually reaching 0.
        if (outcome == PullOutcome.Clear)
        {
            pull.EndHpFraction = 0f;
            pull.LowestHpFraction = 0f;
        }
        else
        {
            var lowest = boss.LowestHpFraction > 0f
                ? boss.LowestHpFraction
                : boss.LastNonZeroHpFraction;
            pull.EndHpFraction = lowest;
            pull.LowestHpFraction = lowest;
        }

        session.LastActiveUtc = DateTime.UtcNow;

        var discard = outcome == PullOutcome.Wipe
                      && config.DiscardShortPulls
                      && pull.Duration < TimeSpan.FromSeconds(config.ShortPullThresholdSeconds);

        if (discard)
        {
            pull.Discarded = true;
            session.DiscardedCount++;
            if (config.DiscardedCountsAsAttempt)
                session.AttemptCounter = pull.Attempt;
            PullDiscarded?.Invoke(pull);
        }
        else
        {
            session.AttemptCounter = pull.Attempt;
            session.Pulls.Add(pull);
            PullCommitted?.Invoke(pull);
            if (outcome == PullOutcome.Clear)
                Cleared?.Invoke(session);
        }

        store.Save();
    }

    private void EndSession()
    {
        if (session == null)
            return;

        // A dangling pull (left mid-fight) resolves as a wipe so it is not lost.
        if (session.Current != null)
            ResolvePull(PullOutcome.Wipe);

        var ended = session;
        session = null;

        // Don't keep sessions that never saw a pull (entered and left).
        if (ended.Pulls.Count == 0 && ended.DiscardedCount == 0)
            store.Remove(ended);
        else
            store.Save();

        SessionEnded?.Invoke(ended);
    }

    // â”€â”€ Death detection â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void DetectFirstDeath()
    {
        if (session?.Current == null)
            return;

        // Inside a duty only the party is present, so the dead player is found straight
        // from the object table, which also gives the object id the cause lookup needs.
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not IPlayerCharacter pc)
                continue;
            if (pc.MaxHp > 0 && pc.CurrentHp == 0)
            {
                RecordFirstDeath(pc.Name.TextValue, pc.GameObjectId);
                return;
            }
        }
    }

    private void RecordFirstDeath(string name, ulong objectId)
    {
        if (session?.Current == null)
            return;

        session.Current.FirstDeath = new DeathInfo
        {
            Name = name,
            Utc = DateTime.UtcNow,
            RecordOffset = getRecordOffset(),
            Cause = getDeathCause(objectId),
        };
    }
}
