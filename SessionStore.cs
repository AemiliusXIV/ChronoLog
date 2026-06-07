// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ChronoLog.Model;

namespace ChronoLog;

/// <summary>
/// Persists sessions to disk so prog data survives an instance reset, a plugin reload, or a
/// game restart. A non-cleared session for the same fight is resumed rather than replaced, so
/// attempts keep counting across all of that.
/// </summary>
public sealed class SessionStore
{
    private const int MaxSessions = 50;
    private static readonly TimeSpan ResumeWindow = TimeSpan.FromHours(12);

    private readonly string path;

    public List<RaidSession> Sessions { get; private set; } = new();

    public RaidSession? MostRecent => Sessions.Count == 0 ? null : Sessions[^1];

    public SessionStore()
    {
        path = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "sessions.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(path))
                Sessions = JsonConvert.DeserializeObject<List<RaidSession>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to load sessions; starting fresh");
            Sessions = new();
        }
    }

    public void Save()
    {
        try
        {
            while (Sessions.Count > MaxSessions)
                Sessions.RemoveAt(0);
            File.WriteAllText(path, JsonConvert.SerializeObject(Sessions, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to save sessions");
        }
    }

    /// <summary>A recent, non-cleared session for this fight that should be continued, if any.</summary>
    public RaidSession? FindResumable(uint territoryId) =>
        Sessions.LastOrDefault(s =>
            s.TerritoryId == territoryId &&
            !s.Cleared &&
            DateTime.UtcNow - s.LastActiveUtc < ResumeWindow);

    public void Add(RaidSession session)
    {
        Sessions.Add(session);
        Save();
    }

    public void Remove(RaidSession session)
    {
        Sessions.Remove(session);
        Save();
    }
}
