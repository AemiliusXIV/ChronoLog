// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ChronoLog.Model;
using ChronoLog.Output;

namespace ChronoLog.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private Configuration Config => plugin.Config;

    private string status = string.Empty;
    private Guid selectedId = Guid.Empty;

    // Per-session pull count at the time of the last clipboard copy.
    // "Copy new" appears once new pulls arrive after that snapshot, tracked independently
    // per session so switching sessions doesn't invalidate the other session's state.
    private readonly Dictionary<Guid, int> lastCopyPullCounts = new();

    public MainWindow(Plugin plugin) : base("ChronoLog##cl_main")
    {
        this.plugin = plugin;
        Size = new Vector2(640, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 260),
            MaximumSize = new Vector2(4000, 4000),
        };
    }

    private static readonly Vector4 WarnColor = new(1f, 0.75f, 0.25f, 1f);

    public override void Draw()
    {
        DrawObsOutputWarning();
        DrawSessionPicker();

        var session = ResolveSelected();
        if (session == null || session.Pulls.Count == 0)
        {
            ImGui.TextDisabled("No pulls captured yet. Start a duty and the list fills in as you go.");
            DrawLiveBoss();
            return;
        }

        var live = plugin.DutyTracker.Session == session;
        ImGui.TextUnformatted(session.FightName);
        ImGui.SameLine();
        ImGui.TextDisabled(live ? "(live)" : "(stored)");

        var committedCount = session.Pulls.Count(p => !p.Discarded);
        var briefCount = session.Pulls.Count(p => p.Discarded);
        var bestRemaining = (int)Math.Round(session.BestHpFraction * 100);
        var bestText = session.Cleared ? "cleared" : $"best pull: boss to {bestRemaining}%";
        var briefNote = briefCount > 0 ? $"  ·  {briefCount} brief attempt(s)" : string.Empty;
        ImGui.TextDisabled($"{committedCount} pull(s) registered  ·  {bestText}{briefNote}");

        DrawTable(session);

        ImGui.Spacing();
        if (ImGui.Button("Copy description block"))
        {
            var block = TextExporter.Build(session, Config.TemplateFormat, Config.EffectiveTimestampOffset(), Config.PhaseTimestampsEnabled, Config.PhaseTimestampOffsetSeconds);
            ImGui.SetClipboardText(block);
            lastCopyPullCounts[session.Id] = session.Pulls.Count;
            status = "Copied to clipboard.";
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copy all pulls from this session.");

        // New committed (non-brief) pulls since the last copy for this session.
        // Only show "Copy new" once the user has done an initial copy - otherwise there's
        // no "0:00 Stream start" header in their description yet and a partial paste breaks YouTube chapters.
        lastCopyPullCounts.TryGetValue(session.Id, out var lastCount);
        var newPulls = lastCopyPullCounts.ContainsKey(session.Id)
            ? session.Pulls.Skip(lastCount).Where(p => !p.Discarded).ToList()
            : new List<PullEntry>();
        if (newPulls.Count > 0)
        {
            ImGui.SameLine();
            var rangeLabel = newPulls.Count == 1
                ? $"Copy new (pull {newPulls[0].Attempt})"
                : $"Copy new (pulls {newPulls[0].Attempt}-{newPulls[^1].Attempt})";
            if (ImGui.Button(rangeLabel))
            {
                var block = TextExporter.Build(session, Config.TemplateFormat, Config.EffectiveTimestampOffset(), Config.PhaseTimestampsEnabled, Config.PhaseTimestampOffsetSeconds, startIndex: lastCount);
                ImGui.SetClipboardText(block);
                lastCopyPullCounts[session.Id] = session.Pulls.Count;
                status = $"Copied {newPulls.Count} new pull(s).";
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Copy only the pulls added since the last copy.\nPaste this at the end of your existing description block.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Write to file"))
        {
            try
            {
                var block = TextExporter.Build(session, Config.TemplateFormat, Config.EffectiveTimestampOffset(), Config.PhaseTimestampsEnabled, Config.PhaseTimestampOffsetSeconds);
                var path = TextExporter.WriteToFile(plugin.ExportDirectory(), session, block);
                status = $"Wrote {path}";
            }
            catch (Exception ex)
            {
                status = $"Write failed: {ex.Message}";
            }
        }
#if DEBUG
        ImGui.SameLine();
        if (ImGui.Button("Push to YouTube"))
        {
            if (!plugin.YouTube.IsConnected)
                status = "Connect YouTube in settings first.";
            else
            {
                plugin.FlushYouTubeNow();
                status = "Pushing to YouTube...";
            }
        }
#endif

        if (!string.IsNullOrEmpty(status))
            ImGui.TextDisabled(status);

        DrawResetButton(session);
        DrawLiveBoss();
    }

    private void DrawObsOutputWarning()
    {
        if (!Config.ObsEnabled) return;

        if (!plugin.Obs.IsConnected)
        {
            ImGui.TextColored(WarnColor, "OBS not connected - chapter markers won't be sent.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Settings##obsconn"))
                plugin.OpenSettings();
            ImGui.Spacing();
        }
        else if (plugin.Obs.OutputActive == false)
        {
            ImGui.TextColored(WarnColor, "OBS is not recording or streaming - chapter markers won't be sent.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Settings##obsout"))
                plugin.OpenSettings();
            ImGui.Spacing();
        }
    }

    private RaidSession? ResolveSelected()
    {
        if (selectedId != Guid.Empty)
        {
            var found = plugin.Store.Sessions.Find(s => s.Id == selectedId);
            if (found != null)
                return found;
        }
        return plugin.ActiveSession;
    }

    private const string DeletePopupId = "Delete session##cl_del";

    private void DrawSessionPicker()
    {
        var sessions = plugin.Store.Sessions;
        if (sessions.Count <= 1)
            return;

        var current = ResolveSelected();
        var label = current == null ? "(none)" : SessionLabel(current);
        ImGui.SetNextItemWidth(380);
        if (ImGui.BeginCombo("Session", label))
        {
            // Most recent first.
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                var s = sessions[i];
                var isSel = current == s;
                if (ImGui.Selectable(SessionLabel(s), isSel))
                    selectedId = s.Id;
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button("Latest"))
            selectedId = Guid.Empty;

        // Delete button only for explicitly selected stored sessions, never the live one.
        var isStoredSelection = selectedId != Guid.Empty && current != plugin.DutyTracker.Session;
        if (isStoredSelection && current != null)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.48f, 0.12f, 0.12f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.62f, 0.18f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.36f, 0.08f, 0.08f, 1f));
            if (ImGui.Button("Delete"))
                ImGui.OpenPopup(DeletePopupId);
            ImGui.PopStyleColor(3);

            var popupOpen = true;
            if (ImGui.BeginPopupModal(DeletePopupId, ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted($"Delete \"{current.FightName}\" ({current.Pulls.Count(p => !p.Discarded)} pull(s))?");
                ImGui.TextDisabled("This cannot be undone.");
                ImGui.Spacing();
                if (ImGui.Button("Delete", new Vector2(80f, 0f)))
                {
                    plugin.Store.Remove(current);
                    selectedId = Guid.Empty;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(80f, 0f)))
                    ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
        }
    }

    private static string SessionLabel(RaidSession s)
    {
        var when = s.StartedUtc.ToLocalTime().ToString("MM-dd HH:mm");
        var tag = s.Cleared ? "cleared" : $"{s.Pulls.Count(p => !p.Discarded)}p";
        return $"{s.FightName}  ({when}, {tag})";
    }

    private static void DrawTable(RaidSession session)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY;
        if (!ImGui.BeginTable("##pulls", 6, flags, new Vector2(0, 240)))
            return;

        ImGui.TableSetupColumn("Pull", ImGuiTableColumnFlags.WidthFixed, 44);
        ImGui.TableSetupColumn("Outcome", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("End HP", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Phase", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("First death");
        ImGui.TableHeadersRow();

        foreach (var p in session.Pulls)
        {
            ImGui.TableNextRow();

            if (p.Discarded)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.45f, 0.45f, 1f));
                ImGui.TableNextColumn(); ImGui.TextUnformatted(p.Attempt.ToString());
                ImGui.TableNextColumn(); ImGui.TextUnformatted("Brief attempt");
                ImGui.TableNextColumn(); ImGui.TextUnformatted("-");
                ImGui.TableNextColumn(); ImGui.TextUnformatted("-");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(FormatDuration(p.Duration));
                ImGui.TableNextColumn(); ImGui.TextUnformatted("-");
                ImGui.PopStyleColor();
                continue;
            }

            ImGui.TableNextColumn();
            if (p.IsMarked)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.82f, 0.15f, 1f));
                ImGui.TextUnformatted($"★ {p.Attempt}");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.TextUnformatted(p.Attempt.ToString());
            }
            ImGui.TableNextColumn(); ImGui.TextUnformatted(p.Outcome.ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted($"{(int)Math.Round(p.EndHpFraction * 100)}%");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(p.EndPhase ?? "-");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(FormatDuration(p.Duration));
            ImGui.TableNextColumn();
            var death = p.FirstDeath == null
                ? "-"
                : p.FirstDeath.Cause == null
                    ? p.FirstDeath.Name
                    : $"{p.FirstDeath.Name} ({p.FirstDeath.Cause})";
            ImGui.TextUnformatted(death);
        }

        ImGui.EndTable();
    }

    private const string ResetPopupId = "Confirm reset##cl_reset";

    private void DrawResetButton(RaidSession session)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.48f, 0.12f, 0.12f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.62f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.36f, 0.08f, 0.08f, 1f));
        if (ImGui.Button("Reset session"))
        {
            if (Config.ConfirmSessionReset)
                ImGui.OpenPopup(ResetPopupId);
            else
                plugin.ResetSession(session);
        }
        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Clear all recorded pulls and reset the attempt counter.\nAny pull currently in progress continues and becomes Pull 1.");

        var popupOpen = true;
        if (ImGui.BeginPopupModal(ResetPopupId, ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Clear all pulls from this session?");
            ImGui.TextDisabled("The attempt counter resets. This cannot be undone.");
            ImGui.Spacing();

            var noConfirm = !Config.ConfirmSessionReset;
            if (ImGui.Checkbox("Don't ask again", ref noConfirm))
            {
                Config.ConfirmSessionReset = !noConfirm;
                Config.Save();
            }

            ImGui.Spacing();
            if (ImGui.Button("Clear", new Vector2(80f, 0f)))
            {
                plugin.ResetSession(session);
                status = "Session cleared.";
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(80f, 0f)))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    private void DrawLiveBoss()
    {
        var boss = plugin.BossReader;
        if (!boss.HasBoss)
            return;
        ImGui.Separator();
        ImGui.TextDisabled($"Boss: {boss.BossName}  ·  {(int)Math.Round(boss.CurrentHpFraction * 100)}% (low {(int)Math.Round(boss.LowestHpFraction * 100)}%)");
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}h{t.Minutes:00}m{t.Seconds:00}s"
            : $"{(int)t.TotalMinutes}m{t.Seconds:00}s";
    }

    public void Dispose() { }
}
