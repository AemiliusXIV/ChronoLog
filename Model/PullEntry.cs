// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ChronoLog.Model;

/// <summary>
/// One phase entry: the formatted phase name and the moment it became the active phase.
/// Index 0 in a pull's PhaseLog is always P1, recorded at pull start.
/// </summary>
public sealed class PhaseTransitionEntry
{
    /// <summary>Formatted phase label as it was at the moment of entry (e.g. "P2: King Thordan").</summary>
    public string PhaseName { get; set; } = string.Empty;

    /// <summary>OBS stream/recording offset when the transition ability resolved, or null when OBS was off.</summary>
    public TimeSpan? RecordOffset { get; set; }

    /// <summary>Wall-clock UTC time the transition was detected.</summary>
    public DateTime Utc { get; set; }
}

public enum PullOutcome
{
    InProgress = 0,
    Wipe = 1,
    Clear = 2,
    Enrage = 3,
}

/// <summary>First death of a pull: who, when, and what killed them (if known).</summary>
public sealed class DeathInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>OBS stream/recording offset at the moment of death, if OBS was reachable.</summary>
    public TimeSpan? RecordOffset { get; set; }

    /// <summary>Wall-clock time of death.</summary>
    public DateTime Utc { get; set; }

    /// <summary>Ability that dealt the lethal blow. Null when the precise hook is off/unavailable.</summary>
    public string? Cause { get; set; }
}

/// <summary>One attempt at a fight. Pending until it resolves, so its duration is known before commit.</summary>
public sealed class PullEntry
{
    public int Attempt { get; set; }
    public string FightName { get; set; } = string.Empty;

    public DateTime StartUtc { get; set; }
    public DateTime? CombatStartUtc { get; set; }
    public DateTime? EndUtc { get; set; }

    public PullOutcome Outcome { get; set; } = PullOutcome.InProgress;

    /// <summary>Boss HP fraction (0..1) at the end of the pull.</summary>
    public float EndHpFraction { get; set; } = 1f;

    /// <summary>Lowest boss HP fraction (0..1) reached during the pull.</summary>
    public float LowestHpFraction { get; set; } = 1f;

    public string? EndPhase { get; set; }

    public DeathInfo? FirstDeath { get; set; }

    /// <summary>
    /// OBS stream duration (preferred) or recording duration captured at pull start.
    /// This is an absolute VOD timestamp, no normalization applied.
    /// Null if OBS was not connected or neither output was active.
    /// </summary>
    public TimeSpan? RecordOffsetAtStart { get; set; }

    /// <summary>True when the pull was dropped for being shorter than the discard threshold.</summary>
    public bool Discarded { get; set; }

    /// <summary>Set via /chrono mark or the mark overlay button. Toggles off on a second press.</summary>
    public bool IsMarked { get; set; } = false;

    /// <summary>
    /// OBS stream offset at the moment the mark was pressed.
    /// Only set when MarkUsePressTime is on and the pull was still active.
    /// When null and IsMarked is true, the export uses the normal pull-start offset.
    /// </summary>
    public TimeSpan? MarkOffset { get; set; }

    /// <summary>UTC time the mark was applied.</summary>
    public DateTime? MarkUtc { get; set; }

    /// <summary>
    /// Per-phase chapter data, populated when the phase timestamps toggle is on and the fight
    /// has an authored phase table. Index 0 is P1 (recorded at pull start). Subsequent entries
    /// are phase transitions in order of detection. Empty when the toggle is off, or the pull
    /// stayed in P1 (Count == 1), or the fight uses the generic fallback.
    /// </summary>
    public List<PhaseTransitionEntry> PhaseLog { get; set; } = new();

    /// <summary>Duration measured from combat start when known, otherwise from pull start.</summary>
    [JsonIgnore]
    public TimeSpan Duration
    {
        get
        {
            var from = CombatStartUtc ?? StartUtc;
            var to = EndUtc ?? DateTime.UtcNow;
            var span = to - from;
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }
    }

    /// <summary>Duration measured strictly from the "Duty Start" message.</summary>
    [JsonIgnore]
    public TimeSpan DurationFromDutyStart
    {
        get
        {
            var span = (EndUtc ?? DateTime.UtcNow) - StartUtc;
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }
    }
}
