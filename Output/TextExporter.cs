// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text;
using ChronoLog.Model;

namespace ChronoLog.Output;

/// <summary>
/// Turns a session's committed pulls into a YouTube-description block. Timestamps are
/// absolute OBS stream/recording offsets captured at each pull start, so they point to
/// the correct position in the full stream VOD without any manual calculation.
///
/// YouTube chapters need a 0:00 entry to activate â€” that should be in your stream's
/// existing description (e.g. "0:00 Stream start"). The raid block is appended after.
///
/// When OBS is not connected the times fall back to wall-clock offsets relative to the
/// first pull, which are accurate for durations but not for VOD position.
/// </summary>
public static class TextExporter
{
    /// <summary>Renders a template against fixed sample values, for the settings preview.</summary>
    public static string RenderSample(string template) =>
        template
            .Replace("{time}", "45:22")  // sample is >9 min so no leading zero visible
            .Replace("{pull}", "3")
            .Replace("{fight}", "Example Fight")
            .Replace("{phase}", "P2: King Thordan")
            .Replace("{hp}", "12")
            .Replace("{lowhp}", "12")
            .Replace("{duration}", "4m36s")
            .Replace("{outcome}", "wipe")
            .Replace("{cause}", "Akh Morn")
            .Replace("{death}", "Tank");

    public static string Build(
        RaidSession session,
        string template,
        int offsetSeconds = 0,
        bool phaseEnabled = false,
        int phaseOffsetSeconds = -3,
        int startIndex = 0)
    {
        if (session.Pulls.Count == 0)
            return string.Empty;

        startIndex = Math.Clamp(startIndex, 0, session.Pulls.Count);
        if (startIndex >= session.Pulls.Count)
            return string.Empty;

        var first = session.Pulls[0];
        var sb = new StringBuilder();

        // "0:00 Stream start" anchors YouTube chapters for a full export. Skip it for
        // incremental copies - the header is already in the description from the first paste.
        if (startIndex == 0 && first.RecordOffsetAtStart.HasValue)
            sb.AppendLine($"{Timecode(TimeSpan.Zero)}  Stream start");

        for (var i = startIndex; i < session.Pulls.Count; i++)
        {
            var pull = session.Pulls[i];
            if (phaseEnabled && pull.PhaseLog.Count > 1)
                AppendPhaseLines(sb, pull, first, phaseOffsetSeconds);
            else
                sb.AppendLine(RenderLine(pull, first, template, offsetSeconds));
        }
        return sb.ToString().TrimEnd();
    }

    private static void AppendPhaseLines(StringBuilder sb, PullEntry pull, PullEntry first, int phaseOffsetSeconds)
    {
        var log = pull.PhaseLog;
        for (var i = 0; i < log.Count; i++)
        {
            var entry = log[i];
            var isLast = i == log.Count - 1;

            TimeSpan t;
            if (entry.RecordOffset.HasValue)
                t = entry.RecordOffset.Value + TimeSpan.FromSeconds(phaseOffsetSeconds);
            else
                t = (entry.Utc - first.StartUtc) + TimeSpan.FromSeconds(phaseOffsetSeconds);
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;

            var line = $"{Timecode(t)}  Pull {pull.Attempt}, {entry.PhaseName}";
            if (isLast)
            {
                line += pull.Outcome switch
                {
                    PullOutcome.Wipe or PullOutcome.Enrage =>
                        $" [{Outcome(pull.Outcome)} {Percent(pull.EndHpFraction)}%]",
                    PullOutcome.Clear => " [CLEAR]",
                    _ => string.Empty,
                };
                if (pull.IsMarked)
                    line += "  ★";
            }
            sb.AppendLine(line);
        }
    }

    private static string RenderLine(PullEntry pull, PullEntry first, string template, int offsetSeconds)
    {
        // When exact press-time was captured, that overrides the chapter timestamp.
        var offset = (pull.IsMarked && pull.MarkOffset.HasValue)
            ? pull.MarkOffset.Value
            : ComputeOffset(pull, first, offsetSeconds);

        var line = template
            .Replace("{time}", Timecode(offset))
            .Replace("{pull}", pull.Attempt.ToString())
            .Replace("{fight}", pull.FightName)
            .Replace("{phase}", pull.EndPhase ?? string.Empty)
            .Replace("{hp}", Percent(pull.EndHpFraction))
            .Replace("{lowhp}", Percent(pull.LowestHpFraction))
            .Replace("{duration}", Duration(pull.Duration))
            .Replace("{outcome}", Outcome(pull.Outcome))
            .Replace("{cause}", pull.FirstDeath?.Cause ?? string.Empty)
            .Replace("{death}", pull.FirstDeath?.Name ?? string.Empty);

        if (pull.IsMarked)
            line += "  ★";

        return line;
    }

    private static TimeSpan ComputeOffset(PullEntry pull, PullEntry first, int offsetSeconds)
    {
        TimeSpan t;
        if (pull.RecordOffsetAtStart.HasValue)
            // Absolute stream/recording position â€” apply offset to shift the chapter link.
            t = pull.RecordOffsetAtStart.Value + TimeSpan.FromSeconds(offsetSeconds);
        else
            // No OBS data: wall-clock relative to first pull. Offset still applies.
            t = (pull.StartUtc - first.StartUtc) + TimeSpan.FromSeconds(offsetSeconds);

        return t < TimeSpan.Zero ? TimeSpan.Zero : t;
    }

    private static string Timecode(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    private static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}h{t.Minutes:00}m{t.Seconds:00}s"
            : $"{(int)t.TotalMinutes}m{t.Seconds:00}s";
    }

    private static string Percent(float fraction) =>
        ((int)Math.Round(Math.Clamp(fraction, 0f, 1f) * 100)).ToString();

    private static string Outcome(PullOutcome outcome) => outcome switch
    {
        PullOutcome.Clear => "CLEAR",
        PullOutcome.Enrage => "enrage",
        PullOutcome.Wipe => "wipe",
        _ => string.Empty,
    };

    /// <summary>Writes the block to a timestamped file and returns the full path.</summary>
    public static string WriteToFile(string directory, RaidSession session, string content)
    {
        Directory.CreateDirectory(directory);
        var stamp = session.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd_HHmm");
        var fileName = $"{Sanitize(session.FightName)}_{stamp}.txt";
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "session" : name;
    }
}
