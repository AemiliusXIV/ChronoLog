// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ChronoLog.Model;

/// <summary>One run at a fight: pulls accumulate across instance resets and reloads until a clear.</summary>
public sealed class RaidSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public uint TerritoryId { get; set; }
    public string FightName { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Last time a pull was added or resolved, used to decide whether to resume.</summary>
    public DateTime LastActiveUtc { get; set; } = DateTime.UtcNow;

    /// <summary>All resolved pulls, including brief attempts. Committed pulls have Discarded = false.</summary>
    public List<PullEntry> Pulls { get; set; } = new();

    /// <summary>How many short resets were dropped this run.</summary>
    public int DiscardedCount { get; set; }

    /// <summary>The pull currently in progress, or null between pulls. Not persisted.</summary>
    [JsonIgnore]
    public PullEntry? Current { get; set; }

    /// <summary>Running attempt count. Drives the {pull} token.</summary>
    public int AttemptCounter { get; set; }

    [JsonIgnore]
    public bool Cleared => Pulls.Any(p => !p.Discarded && p.Outcome == PullOutcome.Clear);

    /// <summary>Best (lowest) boss HP fraction reached across committed pulls (brief attempts excluded).</summary>
    [JsonIgnore]
    public float BestHpFraction
    {
        get
        {
            var committed = Pulls.Where(p => !p.Discarded).ToList();
            return committed.Count == 0 ? 1f : committed.Min(p => p.LowestHpFraction);
        }
    }
}
