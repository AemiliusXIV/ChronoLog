// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using ChronoLog.Capture;
using ChronoLog.Model;

namespace ChronoLog.Obs;

/// <summary>
/// Drops a live OBS chapter marker when combat starts. Firing off CombatStarted rather than
/// pull start keeps the marker on the actual pull instead of the zone-in moment. Markers are
/// live, so they aren't subject to the short-pull discard the text/YouTube lists apply; a fast
/// reset still leaves a marker, which is harmless and easy to skip while editing.
/// </summary>
public sealed class ObsChapterSink : IDisposable
{
    private readonly Configuration config;
    private readonly ObsWebSocketClient obs;
    private readonly DutyTracker tracker;

    public ObsChapterSink(Configuration config, ObsWebSocketClient obs, DutyTracker tracker)
    {
        this.config = config;
        this.obs = obs;
        this.tracker = tracker;
        tracker.CombatStarted += OnCombatStarted;
    }

    public void Dispose() => tracker.CombatStarted -= OnCombatStarted;

    private void OnCombatStarted(PullEntry pull)
    {
        if (!config.Enabled || !config.ObsEnabled || !config.EmitObsChapters)
            return;

        var name = $"Pull {pull.Attempt} - {pull.FightName}";
        var ok = obs.CreateRecordChapter(name);
        if (!ok && !string.IsNullOrWhiteSpace(config.ObsChapterHotkeyFallback))
            obs.TriggerHotkey(config.ObsChapterHotkeyFallback);
    }
}
