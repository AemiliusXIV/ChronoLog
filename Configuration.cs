// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace ChronoLog;

/// <summary>How an authored phase name is formatted in the {phase} token.</summary>
public enum PhaseLabelStyle
{
    /// <summary>"King Thordan"</summary>
    FormalOnly = 0,

    /// <summary>"P2 King Thordan"</summary>
    NumberSpaceName = 1,

    /// <summary>"P2: King Thordan"</summary>
    NumberColonName = 2,
}

/// <summary>What ChronoLog does when it detects a new OBS stream or recording has started.</summary>
public enum StreamRestartBehavior
{
    /// <summary>Do nothing.</summary>
    Off = 0,

    /// <summary>Print a chat message so the user can manually reset if they want to.</summary>
    NotifyOnly = 1,

    /// <summary>Automatically clear the current session's pull list.</summary>
    AutoReset = 2,
}

/// <summary>When a pending YouTube push is flushed to the video description.</summary>
public enum YouTubeFlushTrigger
{
    /// <summary>Push the full list when the duty is cleared.</summary>
    OnClear = 0,

    /// <summary>Push only when the user clicks the flush button.</summary>
    Manual = 1,

    /// <summary>Push after every resolved pull (keeps a live description current).</summary>
    EveryPull = 2,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    // â”€â”€ Capture â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public PhaseLabelStyle PhaseLabelStyle { get; set; } = PhaseLabelStyle.NumberColonName;

    public bool DiscardShortPulls { get; set; } = true;

    /// <summary>Wipes shorter than this (from combat start) are dropped from the lists.</summary>
    public int ShortPullThresholdSeconds { get; set; } = 20;

    /// <summary>Whether a discarded short reset still increments the attempt counter.</summary>
    public bool DiscardedCountsAsAttempt { get; set; } = false;

    // â”€â”€ Text export â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// <summary>
    /// Effective line template. Set from a preset, or generated from <see cref="CustomTokenOrder"/>
    /// when <see cref="TemplateUseCustom"/> is true. Tokens: {time} {pull} {fight} {phase} {hp}
    /// {lowhp} {duration} {outcome} {cause} {death}. Times are absolute stream/recording offsets.
    /// </summary>
    public string TemplateFormat { get; set; } = "{time}  Pull {pull} - {outcome} {hp}% ({phase}) [{duration}]";

    /// <summary>
    /// Seconds added or subtracted from each chapter timestamp. A negative value (e.g. -2)
    /// makes each chapter link land slightly before the pull starts. Applied to text export
    /// and YouTube push only - OBS embedded chapter markers are unaffected.
    /// Works on top of EncodingLagCompensationSeconds when that is enabled.
    /// </summary>
    public int TimestampOffsetSeconds { get; set; } = 0;

    /// <summary>
    /// When true, adds EncodingLagCompensationSeconds to every exported timestamp to
    /// compensate for OBS encoding/buffering lag (the gap between OBS's stream timer and
    /// the actual encoded video content).
    /// </summary>
    public bool AutoCompensateEncodingLag { get; set; } = false;

    /// <summary>
    /// Seconds to add when AutoCompensateEncodingLag is on. Positive = shift timestamps
    /// forward. Typical range: 1-3 s. Tune by watching a chapter link on a VOD and
    /// adjusting until it lands at the pull start.
    /// </summary>
    public int EncodingLagCompensationSeconds { get; set; } = 2;

    /// <summary>
    /// Combined effective offset for text export: encoding lag compensation (if on)
    /// plus the manual TimestampOffsetSeconds.
    /// </summary>
    public int EffectiveTimestampOffset() =>
        TimestampOffsetSeconds + (AutoCompensateEncodingLag ? EncodingLagCompensationSeconds : 0);

    /// <summary>When true the template is built by ordering tokens rather than typed by hand.</summary>
    public bool TemplateUseCustom { get; set; } = false;

    /// <summary>Ordered token names for the custom builder (without braces).</summary>
    public List<string> CustomTokenOrder { get; set; } = new() { "time", "pull", "outcome", "hp", "phase", "duration" };

    /// <summary>Folder the description block is written to. Empty = plugin config dir.</summary>
    public string TextExportDirectory { get; set; } = string.Empty;

    /// <summary>Write the description block to file automatically when the duty clears.</summary>
    public bool AutoExportOnClear { get; set; } = true;

    // â”€â”€ Phase timestamps â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// <summary>
    /// When enabled, pulls that reach multiple phases each expand to one chapter line per
    /// phase in the text export. Pulls staying in P1 are unaffected. Only fights with an
    /// authored phase table produce phase log data.
    /// </summary>
    public bool PhaseTimestampsEnabled { get; set; } = false;

    /// <summary>
    /// Seconds applied to each phase chapter timestamp. Phase transitions fire when the
    /// transition ability resolves (end of cast bar), so -3 to -4 seconds typically lands
    /// at the start of the cast, just as the previous phase ends.
    /// </summary>
    public int PhaseTimestampOffsetSeconds { get; set; } = -3;

    // â”€â”€ OBS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public bool ObsEnabled { get; set; } = false;
    public string ObsHost { get; set; } = "127.0.0.1";
    public int ObsPort { get; set; } = 4455;
    public string ObsPassword { get; set; } = string.Empty;

    /// <summary>Drop a native OBS chapter marker (CreateRecordChapter) on each event.</summary>
    public bool EmitObsChapters { get; set; } = true;

    /// <summary>Hotkey name to trigger when CreateRecordChapter is unavailable. Empty = disabled.</summary>
    public string ObsChapterHotkeyFallback { get; set; } = string.Empty;

    /// <summary>Print a chat warning on duty start when OBS is enabled but not connected.</summary>
    public bool WarnOnObsDisconnected { get; set; } = true;

    /// <summary>
    /// What to do when a new OBS stream or recording is detected (previous one ended and a
    /// fresh one started). Timestamps from the old stream are no longer valid VOD positions,
    /// so clearing the session keeps the export accurate.
    /// </summary>
    public StreamRestartBehavior StreamRestartBehavior { get; set; } = StreamRestartBehavior.NotifyOnly;

    // â”€â”€ Session management â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// <summary>Show a confirmation dialog before clearing a session's pull list.</summary>
    public bool ConfirmSessionReset { get; set; } = true;

    /// <summary>
    /// When re-entering the same duty within 12 hours, resume the existing session rather
    /// than starting a new one. Covers both reloads within a game session and full restarts.
    /// </summary>
    public bool ResumeSessionAcrossRestarts { get; set; } = true;

    // â”€â”€ Mark overlay â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// <summary>Show the floating mark button when inside an instanced duty.</summary>
    public bool ShowMarkOverlay { get; set; } = false;

    /// <summary>
    /// When marking an active pull, store the current stream offset rather than the pull-start
    /// offset. The exported chapter line will point to when you pressed mark, not when the pull began.
    /// </summary>
    public bool MarkUsePressTime { get; set; } = false;

    // â”€â”€ YouTube (opt-in) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public bool YouTubeEnabled { get; set; } = false;

    /// <summary>OAuth client id from the user's own Google Cloud project.</summary>
    public string YouTubeClientId { get; set; } = string.Empty;

    /// <summary>OAuth client secret from the user's own Google Cloud project.</summary>
    public string YouTubeClientSecret { get; set; } = string.Empty;

    public YouTubeFlushTrigger YouTubeFlush { get; set; } = YouTubeFlushTrigger.OnClear;

    /// <summary>
    /// Target video id. When empty the plugin tries to resolve the active live
    /// broadcast for the authorised channel at flush time.
    /// </summary>
    public string YouTubeVideoId { get; set; } = string.Empty;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
