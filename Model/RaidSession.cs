// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog
// SPDX-License-Identifier: GPL-3.0-or-later

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

    /// <summary>Resolved pulls that survived the short-pull filter, in order.</summary>
    public List<PullEntry> Pulls { get; set; } = new();

    /// <summary>How many short resets were dropped this run.</summary>
    public int DiscardedCount { get; set; }

    /// <summary>The pull currently in progress, or null between pulls. Not persisted.</summary>
    [JsonIgnore]
    public PullEntry? Current { get; set; }

    /// <summary>Running attempt count. Drives the {pull} token.</summary>
    public int AttemptCounter { get; set; }

    [JsonIgnore]
    public bool Cleared => Pulls.Any(p => p.Outcome == PullOutcome.Clear);

    /// <summary>Best (lowest) boss HP fraction reached across all committed pulls.</summary>
    [JsonIgnore]
    public float BestHpFraction => Pulls.Count == 0 ? 1f : Pulls.Min(p => p.LowestHpFraction);
}
