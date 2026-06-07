// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;

namespace ChronoLog.Obs;

/// <summary>
/// Thin wrapper over obs-websocket v5. Tracks connection state, exposes the current
/// recording offset (for timestamps) and the native chapter-marker request. All calls
/// are best-effort: a disconnected or unhappy OBS never throws into the caller.
/// </summary>
public sealed class ObsWebSocketClient : IDisposable
{
    private readonly OBSWebsocket obs = new();
    private bool manualDisconnect;

    public bool IsConnected { get; private set; }
    public bool IsConnecting { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    /// Set by the background websocket thread when an unexpected disconnect occurs.
    /// Polled (and cleared) by the framework-thread reconnect loop in Plugin.cs.
    /// </summary>
    private volatile bool pendingUnexpectedDisconnect;

    public bool ConsumePendingDisconnect()
    {
        if (!pendingUnexpectedDisconnect) return false;
        pendingUnexpectedDisconnect = false;
        return true;
    }

    // Stream-restart detection state (all accessed on framework thread via PollStatus).
    // A restart is confirmed when: the stream was previously mature (ran > 90s), then
    // stopped, and a new stream/recording appears with a small offset (< 20s).
    private static readonly TimeSpan PollInterval    = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaturityWindow  = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan FreshStartLimit = TimeSpan.FromSeconds(20);

    private DateTime lastPollTime     = DateTime.MinValue;
    private bool hadMatureStream      = false;  // saw an offset >= MaturityWindow
    private bool streamWasActive      = false;  // stream/recording was returning a value last poll
    private TimeSpan? lastSeenOffset;           // offset value from last active poll
    private bool pendingStreamRestart = false;

    /// <summary>
    /// Whether OBS is actively streaming or recording, based on the last poll.
    /// Null until the first poll completes - use <c>== false</c> to distinguish
    /// "definitely idle" from "not yet checked".
    /// </summary>
    public bool? OutputActive { get; private set; }

    public bool ConsumePendingStreamRestart()
    {
        if (!pendingStreamRestart) return false;
        pendingStreamRestart = false;
        return true;
    }

    /// <summary>
    /// Poll OBS output state and detect stream/recording restarts.
    /// Safe to call every frame - internally throttled to PollInterval.
    /// Must be called from the framework (game main) thread only.
    /// </summary>
    public void PollStatus()
    {
        if (!IsConnected) return;
        if (DateTime.UtcNow - lastPollTime < PollInterval) return;
        lastPollTime = DateTime.UtcNow;

        var offset = GetOffset();

        OutputActive = offset.HasValue;

        if (offset.HasValue)
        {
            // Two ways a restart can be detected:
            // 1. We saw the stream stop last poll, then a fresh offset appears (slow restart).
            // 2. The offset went backwards vs. last poll - stream restarted within the poll
            //    window so we never saw the gap (quick restart / restart in-instance).
            var wentBackwards = streamWasActive
                && lastSeenOffset.HasValue
                && offset.Value < lastSeenOffset.Value - TimeSpan.FromSeconds(5);

            if (hadMatureStream && offset.Value < FreshStartLimit && (!streamWasActive || wentBackwards))
                pendingStreamRestart = true;

            if (offset.Value >= MaturityWindow)
                hadMatureStream = true;

            lastSeenOffset = offset;
            streamWasActive = true;
        }
        else
        {
            // No active stream or recording. Flag the gap so we can catch the restart.
            streamWasActive = false;
            lastSeenOffset = null;
        }
    }

    public event Action<bool>? ConnectionChanged;

    public ObsWebSocketClient()
    {
        obs.Connected += (_, _) =>
        {
            IsConnected = true;
            IsConnecting = false;
            LastError = null;
            lastPollTime = DateTime.MinValue;  // force OutputActive to update on the very next PollStatus call
            Plugin.Log.Information("OBS connected");
            ConnectionChanged?.Invoke(true);
        };
        obs.Disconnected += (_, info) =>
        {
            IsConnected = false;
            IsConnecting = false;
            OutputActive = null;   // unknown until next successful poll
            streamWasActive = false;  // reset so restart detection works correctly on next connect
            lastSeenOffset = null;
            LastError = manualDisconnect ? null : DescribeDisconnect(info);
            if (!manualDisconnect)
                pendingUnexpectedDisconnect = true;
            manualDisconnect = false;
            Plugin.Log.Information("OBS disconnected");
            ConnectionChanged?.Invoke(false);
        };
    }

    public void Connect(string host, int port, string password)
    {
        try
        {
            IsConnecting = true;
            LastError = null;
            manualDisconnect = false;
            obs.ConnectAsync($"ws://{host}:{port}", password ?? string.Empty);
        }
        catch (Exception ex)
        {
            IsConnecting = false;
            LastError = ex.Message;
            Plugin.Log.Warning(ex, "OBS connect failed");
        }
    }

    public void Disconnect()
    {
        try
        {
            manualDisconnect = true;
            IsConnecting = false;
            if (IsConnected)
                obs.Disconnect();
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "OBS disconnect threw");
        }
    }

    private static string DescribeDisconnect(ObsDisconnectionInfo? info)
    {
        var reason = info?.DisconnectReason;
        return string.IsNullOrWhiteSpace(reason)
            ? "Check that OBS is running and the WebSocket server is enabled."
            : reason;
    }

    /// <summary>
    /// Current position for chapter timestamps. Prefers stream duration (the live VOD
    /// timestamp) and falls back to recording duration for recording-only setups.
    /// Returns null when OBS is not connected or neither output is active.
    /// </summary>
    public TimeSpan? GetOffset() => GetStreamOffset() ?? GetRecordOffset();

    /// <summary>Stream duration, or null if OBS is not connected or not streaming.</summary>
    public TimeSpan? GetStreamOffset()
    {
        try
        {
            if (!IsConnected) return null;
            var resp = obs.SendRequest("GetStreamStatus");
            if (resp == null) return null;
            if (!(resp["outputActive"]?.Value<bool>() ?? false)) return null;
            var ms = resp["outputDuration"]?.Value<long>() ?? 0L;
            return ms > 0 ? TimeSpan.FromMilliseconds(ms) : null;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "GetStreamStatus failed");
            return null;
        }
    }

    /// <summary>Recording duration, or null if OBS is not connected or not recording.</summary>
    public TimeSpan? GetRecordOffset()
    {
        try
        {
            if (!IsConnected)
                return null;
            var status = obs.GetRecordStatus();
            if (status == null || !status.IsRecording)
                return null;
            return TimeSpan.FromMilliseconds(status.RecordingDuration);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "GetRecordStatus failed");
            return null;
        }
    }

    /// <summary>
    /// Drops a native chapter marker at the current recording position. Needs OBS 30.2+
    /// recording to Hybrid MP4 to actually embed; returns false (so a fallback can run)
    /// when the request is rejected or OBS is offline.
    /// </summary>
    public bool CreateRecordChapter(string chapterName)
    {
        try
        {
            if (!IsConnected)
                return false;
            var data = new JObject { ["chapterName"] = chapterName };
            obs.SendRequest("CreateRecordChapter", data);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "CreateRecordChapter rejected (needs OBS 30.2+ Hybrid MP4)");
            return false;
        }
    }

    public void TriggerHotkey(string hotkeyName)
    {
        try
        {
            if (IsConnected && !string.IsNullOrWhiteSpace(hotkeyName))
                obs.TriggerHotkeyByName(hotkeyName);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "TriggerHotkeyByName failed");
        }
    }

    public void Dispose()
    {
        Disconnect();
    }
}
