// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog

using ChronoLog.Capture;

namespace ChronoLog.Phases;

/// <summary>
/// Names the current phase of a pull. Authored fights drive phases off boss abilities
/// (via the ActionEffect hook) using the formal names in <see cref="PhaseTables"/>.
/// Fights with no table fall back to generic P1/P2/... counted from boss targetable flips.
/// State is per-pull; call <see cref="BeginPull"/> at each pull start.
/// </summary>
public sealed class PhaseResolver
{
    private readonly Configuration config;

    private int phaseIndex;
    private bool wasTargetable;
    private string? authoredName;
    private bool authored;

    public PhaseResolver(Configuration config)
    {
        this.config = config;
    }

    public string CurrentPhase
    {
        get
        {
            if (authoredName == null)
                return $"P{phaseIndex}";
            return config.PhaseLabelStyle switch
            {
                PhaseLabelStyle.FormalOnly => authoredName,
                PhaseLabelStyle.NumberSpaceName => $"P{phaseIndex} {authoredName}",
                _ => $"P{phaseIndex}: {authoredName}",
            };
        }
    }

    public void BeginPull(uint territoryId)
    {
        phaseIndex = 1;
        wasTargetable = true;

        var fight = PhaseTables.For(territoryId);
        authored = fight != null;
        authoredName = fight?.Phase1Name;
    }

    /// <summary>Generic fallback only. Authored fights advance through <see cref="NoteAction"/>.</summary>
    public void Tick(BossHpReader boss)
    {
        if (authored || !boss.HasBoss)
            return;

        if (!wasTargetable && boss.IsBossTargetable)
            phaseIndex++;
        wasTargetable = boss.IsBossTargetable;
    }

    /// <summary>
    /// An ability fired; advances the phase if it marks a new one for this fight.
    /// Returns the new formatted phase name when a transition occurs, null otherwise.
    /// </summary>
    public string? NoteAction(uint territoryId, uint actionId)
    {
        if (!authored)
            return null;
        var fight = PhaseTables.For(territoryId);
        if (fight == null)
            return null;
        if (fight.Transitions.TryGetValue(actionId, out var name) && name != authoredName)
        {
            phaseIndex++;
            authoredName = name;
            return CurrentPhase;
        }
        return null;
    }
}
