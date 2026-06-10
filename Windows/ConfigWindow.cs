// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog

using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ChronoLog.Output;

namespace ChronoLog.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private Configuration Config => plugin.Config;

    private static readonly Vector4 HeaderColor = new(0.65f, 0.88f, 1f, 0.95f);
    private static readonly Vector4 WarnColor = new(1f, 0.75f, 0.35f, 1f);

    private const string YtConfirmPopup = "Connect to YouTube?##rts_yt_confirm";
    private const string YtMissingPopup = "Credentials needed##rts_yt_missing";

    public ConfigWindow(Plugin plugin) : base("ChronoLog Settings##cl_cfg")
    {
        this.plugin = plugin;
        Size = new Vector2(540, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 240),
            MaximumSize = new Vector2(4000, 4000),
        };
    }

    public override void Draw()
    {
        var enabled = Config.Enabled;
        if (ImGui.Checkbox("Plugin enabled", ref enabled))
        {
            Config.Enabled = enabled;
            Config.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Open session window"))
            plugin.OpenMain();

        DrawCapture();
        DrawTextExport();
        DrawObs();
        DrawMark();
        DrawYouTube();
    }

    private void DrawCapture()
    {
        DrawSectionHeader("Capture");

        var style = (int)Config.PhaseLabelStyle;
        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("Phase label", ref style, "King Thordan\0P2 King Thordan\0P2: King Thordan\0\0"))
        {
            Config.PhaseLabelStyle = (PhaseLabelStyle)style;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How named phases appear in the {phase} field. Fights without phase data show P1/P2/...");

        var discard = Config.DiscardShortPulls;
        if (ImGui.Checkbox("Discard short pulls", ref discard))
        {
            Config.DiscardShortPulls = discard;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Wipes shorter than the threshold (from combat start) are shown as dimmed 'Brief attempt' rows\nand excluded from the text export. The attempt counter always increments for them so\nyour pull numbering stays in sync with FFLogs.");

        if (Config.DiscardShortPulls)
        {
            ImGui.Indent();
            var threshold = Config.ShortPullThresholdSeconds;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Threshold (seconds)##shortpull", ref threshold))
            {
                Config.ShortPullThresholdSeconds = Math.Clamp(threshold, 1, 600);
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Default 15 s matches the empirical FFLogs threshold for brief attempts.");
            ImGui.Unindent();
        }

        ImGui.Spacing();

        var resume = Config.ResumeSessionAcrossRestarts;
        if (ImGui.Checkbox("Resume session after game restart", ref resume))
        {
            Config.ResumeSessionAcrossRestarts = resume;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When re-entering the same duty within 12 hours, continue the existing pull list\nrather than starting fresh. Covers both plugin reloads and full game restarts.");

        var confirmReset = Config.ConfirmSessionReset;
        if (ImGui.Checkbox("Ask before clearing a session", ref confirmReset))
        {
            Config.ConfirmSessionReset = confirmReset;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Shows a confirmation prompt when you click 'Reset session'.\nCan also be toggled from within the prompt itself.");
    }

    private static readonly (string Label, string Template)[] Presets =
    {
        ("Pull, outcome, HP, phase, duration", "{time}  Pull {pull} - {outcome} {hp}% ({phase}) [{duration}]"),
        ("Pull, outcome, HP", "{time}  Pull {pull} - {outcome} {hp}%"),
        ("Phase, HP, first death", "{time}  Pull {pull} - {phase} {hp}% - {death} ({cause})"),
        ("Minimal", "{time}  Pull {pull} {outcome}"),
    };

    private static readonly string[] AllTokens =
        { "time", "pull", "fight", "outcome", "hp", "lowhp", "phase", "duration", "cause", "death" };

    private void DrawTextExport()
    {
        DrawSectionHeader("Text export");

        var selected = Config.TemplateUseCustom ? Presets.Length : MatchingPresetIndex();
        ImGui.SetNextItemWidth(320);
        if (ImGui.Combo("Line format", ref selected, BuildPresetCombo()))
        {
            if (selected >= Presets.Length)
            {
                Config.TemplateUseCustom = true;
                Config.TemplateFormat = BuildCustomTemplate(Config.CustomTokenOrder);
            }
            else
            {
                Config.TemplateUseCustom = false;
                Config.TemplateFormat = Presets[selected].Template;
            }
            Config.Save();
        }

        if (Config.TemplateUseCustom)
            DrawCustomBuilder();

        var prevSession = plugin.ActiveSession;
        var previewText = (prevSession != null && prevSession.Pulls.Any(p => !p.Discarded))
            ? TextExporter.RenderPreview(prevSession, Config.TemplateFormat, Config.EffectiveTimestampOffset())
            : TextExporter.RenderSample(Config.TemplateFormat);
        ImGui.TextWrapped($"Preview: {previewText}");

        var compensate = Config.AutoCompensateEncodingLag;
        if (ImGui.Checkbox("Compensate for encoding lag", ref compensate))
        {
            Config.AutoCompensateEncodingLag = compensate;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "OBS's stream/recording timer starts counting immediately, but the\n" +
                "actual encoded video lags a second or two behind due to buffering.\n" +
                "Enabling this adds a fixed forward shift so chapter links land on\n" +
                "the real video moment rather than slightly before it.\n\n" +
                "Tune the value by clicking a chapter link on a finished VOD and\n" +
                "adjusting until it lands right at the pull start. Then use the\n" +
                "offset below to set your preferred lead-in (e.g. -2 to show the\n" +
                "boss for two seconds before combat).");

        if (Config.AutoCompensateEncodingLag)
        {
            ImGui.Indent();
            var lagComp = Config.EncodingLagCompensationSeconds;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Encoding lag (s)##lagcomp", ref lagComp))
            {
                Config.EncodingLagCompensationSeconds = Math.Clamp(lagComp, 0, 10);
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    "How many seconds ahead OBS's timer runs vs. the encoded video.\n" +
                    "Typical values: 1-2 s with hardware encoding (NVENC/AMF),\n" +
                    "2-4 s with software encoding (x264 at slower presets).\n" +
                    "Start at 2, watch a VOD, and tune from there.");
            ImGui.Unindent();
        }

        var tsOffset = Config.TimestampOffsetSeconds;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Timestamp offset (s)", ref tsOffset))
        {
            Config.TimestampOffsetSeconds = Math.Clamp(tsOffset, -300, 0);
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Shifts every chapter timestamp back by this many seconds.\n" +
                "-2 makes the chapter link land 2 seconds before the pull starts,\n" +
                "so viewers see the boss before combat begins.\n" +
                "Applied on top of the encoding lag compensation above.\n\n" +
                "Does not affect OBS embedded chapter markers.");

        ImGui.Spacing();
        var hasActiveSession = plugin.DutyTracker.Session != null;
        if (hasActiveSession) ImGui.BeginDisabled();
        var phaseEnabled = Config.PhaseTimestampsEnabled;
        if (ImGui.Checkbox("Expand phases as separate timestamps", ref phaseEnabled))
        {
            Config.PhaseTimestampsEnabled = phaseEnabled;
            Config.Save();
        }
        if (hasActiveSession) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(hasActiveSession
                ? "Can't change mid-session. Takes effect at the next session start."
                : "When on, pulls that reach multiple phases each get one chapter line per phase.\n" +
                  "A pull that stays in P1 is left as a single line.\n\n" +
                  "Only fights with an authored phase table produce phase data\n" +
                  "(all Ultimates, M4S, M8S, M12S, Dancing Mad).");

        if (Config.PhaseTimestampsEnabled)
        {
            ImGui.Indent();
            var phaseOffset = Config.PhaseTimestampOffsetSeconds;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Phase offset (s)", ref phaseOffset))
            {
                Config.PhaseTimestampOffsetSeconds = Math.Clamp(phaseOffset, -30, 10);
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    "Shifts each phase chapter by this many seconds. Negative values pull\n" +
                    "the link back before the transition; -3 to -4 typically lands at the\n" +
                    "start of the transition cast. Positive values push it into the new phase.\n" +
                    "0 = exact moment the ability resolved.");
            ImGui.Unindent();
        }

        ImGui.TextDisabled("Export folder (blank = Documents\\ChronoLog)");
        var dir = Config.TextExportDirectory;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##exportdir", ref dir, 512))
        {
            Config.TextExportDirectory = dir;
            Config.Save();
        }
        if (ImGui.Button("Open export folder"))
            plugin.OpenExportFolder();

        var auto = Config.AutoExportOnClear;
        if (ImGui.Checkbox("Write a file automatically on clear", ref auto))
        {
            Config.AutoExportOnClear = auto;
            Config.Save();
        }
    }

    private void DrawCustomBuilder()
    {
        ImGui.Indent();
        ImGui.TextDisabled("Order the fields:");

        var order = Config.CustomTokenOrder;
        for (int i = 0; i < order.Count; i++)
        {
            ImGui.PushID(i);
            if (ImGui.Button("^") && i > 0)
            {
                (order[i - 1], order[i]) = (order[i], order[i - 1]);
                CommitCustom();
            }
            ImGui.SameLine();
            if (ImGui.Button("v") && i < order.Count - 1)
            {
                (order[i + 1], order[i]) = (order[i], order[i + 1]);
                CommitCustom();
            }
            ImGui.SameLine();
            if (ImGui.Button("x"))
            {
                order.RemoveAt(i);
                CommitCustom();
                ImGui.PopID();
                break;
            }
            ImGui.SameLine();
            ImGui.TextUnformatted(order[i]);
            ImGui.PopID();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Add:");
        foreach (var tok in AllTokens)
        {
            if (order.Contains(tok))
                continue;
            ImGui.SameLine();
            if (ImGui.Button(tok))
            {
                order.Add(tok);
                CommitCustom();
            }
        }
        ImGui.Unindent();
    }

    private void CommitCustom()
    {
        Config.TemplateFormat = BuildCustomTemplate(Config.CustomTokenOrder);
        Config.Save();
    }

    private static string BuildCustomTemplate(System.Collections.Generic.List<string> order)
    {
        if (order.Count == 0)
            return "{time}";
        if (order[0] == "time")
        {
            var rest = order.GetRange(1, order.Count - 1).ConvertAll(t => "{" + t + "}");
            return rest.Count == 0 ? "{time}" : "{time}  " + string.Join(" - ", rest);
        }
        return string.Join(" - ", order.ConvertAll(t => "{" + t + "}"));
    }

    private int MatchingPresetIndex()
    {
        for (int i = 0; i < Presets.Length; i++)
            if (Presets[i].Template == Config.TemplateFormat)
                return i;
        return 0;
    }

    private static string BuildPresetCombo()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in Presets)
            sb.Append(p.Label).Append('\0');
        sb.Append("Custom (build by ordering)").Append('\0').Append('\0');
        return sb.ToString();
    }

    private void DrawObs()
    {
        DrawSectionHeader("OBS");

        if (ImGui.TreeNode("Setup guide##obs_guide"))
        {
            ImGui.TextWrapped("1. In OBS, open Tools > WebSocket Server Settings.");
            ImGui.TextWrapped("2. Enable the server and note the port (default 4455).");
            ImGui.TextWrapped("3. If you set a password, paste it in the field below.");
            ImGui.TextWrapped("4. For chapter markers to embed in recordings, set OBS to");
            ImGui.TextWrapped("   record in Hybrid MP4 (Settings > Output > Recording).");
            ImGui.TreePop();
        }

        ImGui.Spacing();

        var obsEnabled = Config.ObsEnabled;
        if (ImGui.Checkbox("Enable OBS connection", ref obsEnabled))
        {
            Config.ObsEnabled = obsEnabled;
            Config.Save();
            if (!obsEnabled)
                plugin.Obs.Disconnect();
        }

        var host = Config.ObsHost;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputText("Host", ref host, 128))
        {
            Config.ObsHost = host;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("127.0.0.1 is correct when OBS is on this machine.\nOnly change this if OBS is running on a different PC on your network.");

        var port = Config.ObsPort;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Port", ref port))
        {
            Config.ObsPort = Math.Clamp(port, 1, 65535);
            Config.Save();
        }

        var pw = Config.ObsPassword;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("Password", ref pw, 256, ImGuiInputTextFlags.Password))
        {
            Config.ObsPassword = pw;
            Config.Save();
        }

        if (plugin.Obs.IsConnected)
        {
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.55f, 0.9f), "Connected");
            ImGui.SameLine();
            if (ImGui.Button("Disconnect"))
                plugin.Obs.Disconnect();
        }
        else if (plugin.Obs.IsConnecting)
        {
            ImGui.TextColored(WarnColor, "Connecting...");
        }
        else
        {
            ImGui.TextDisabled("Not connected");
            ImGui.SameLine();
            if (ImGui.Button("Connect"))
                plugin.Obs.Connect(Config.ObsHost, Config.ObsPort, Config.ObsPassword);
        }

        if (!plugin.Obs.IsConnected && !string.IsNullOrEmpty(plugin.Obs.LastError))
            ImGui.TextColored(WarnColor, plugin.Obs.LastError);

        var warnDisconnect = Config.WarnOnObsDisconnected;
        if (ImGui.Checkbox("Warn in chat when entering a duty disconnected", ref warnDisconnect))
        {
            Config.WarnOnObsDisconnected = warnDisconnect;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Prints a chat reminder on duty start when OBS is not connected, or\nconnected but not recording/streaming.");

        ImGui.Spacing();
        ImGui.TextUnformatted("When a new stream or recording is detected:");
        ImGui.SetNextItemWidth(200);
        var restartBehavior = (int)Config.StreamRestartBehavior;
        if (ImGui.Combo("##streamrestart", ref restartBehavior, "Off\0Notify only\0Auto-reset\0"))
        {
            Config.StreamRestartBehavior = (StreamRestartBehavior)restartBehavior;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Off: do nothing.\n" +
                "Notify only: print a chat message so you can reset manually.\n" +
                "Auto-reset: clear the session automatically when a fresh stream starts.\n\n" +
                "A new stream is detected when output stops then restarts from a small\n" +
                "offset (< 20 s). Reconnecting mid-stream is not treated as a restart.");

        ImGui.Spacing();
        var chapters = Config.EmitObsChapters;
        if (ImGui.Checkbox("Drop a chapter marker on each pull", ref chapters))
        {
            Config.EmitObsChapters = chapters;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Needs OBS 30.2+ recording to Hybrid MP4 for chapters to embed.");

        ImGui.TextDisabled("Chapter hotkey fallback (optional)");
        var hotkey = Config.ObsChapterHotkeyFallback;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##obshotkey", ref hotkey, 128))
        {
            Config.ObsChapterHotkeyFallback = hotkey;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Name of an OBS hotkey to trigger as a fallback when the native chapter\n" +
                "marker API is unavailable. Must match exactly the name shown in\n" +
                "OBS Settings > Hotkeys. Leave blank to disable.");
    }

    private void DrawMark()
    {
        DrawSectionHeader("Mark");

        ImGui.TextDisabled("Use /chrono mark (or the overlay button) to flag a pull.");
        ImGui.Spacing();

        var showOverlay = Config.ShowMarkOverlay;
        if (ImGui.Checkbox("Show mark button overlay in instanced duties", ref showOverlay))
        {
            Config.ShowMarkOverlay = showOverlay;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A small native-style button appears near the server clock. Drag it to reposition.");

        if (Config.ShowMarkOverlay)
        {
            ImGui.Indent();

            var pressTime = Config.MarkUsePressTime;
            if (ImGui.Checkbox("Record exact press time", ref pressTime))
            {
                Config.MarkUsePressTime = pressTime;
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When marking a pull still in progress, stores the current stream offset.\nThe exported chapter will link to that moment rather than the pull start.");

            ImGui.Unindent();
        }
    }

    private void DrawYouTube()
    {
        DrawSectionHeader("YouTube (optional)");

        ImGui.TextColored(WarnColor, "Pushing to YouTube needs your own Google Cloud OAuth client.");
        ImGui.TextWrapped("The token is stored locally on this PC and grants broad channel management (YouTube has no description-only scope). Revoke any time in your Google account's third-party access.");
        ImGui.Spacing();

        var ytEnabled = Config.YouTubeEnabled;
        if (ImGui.Checkbox("Enable YouTube push", ref ytEnabled))
        {
            Config.YouTubeEnabled = ytEnabled;
            Config.Save();
        }

        ImGui.TextDisabled("OAuth client id");
        var id = Config.YouTubeClientId;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##ytid", ref id, 256))
        {
            Config.YouTubeClientId = id;
            Config.Save();
        }

        ImGui.TextDisabled("OAuth client secret");
        var secret = Config.YouTubeClientSecret;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##ytsecret", ref secret, 256, ImGuiInputTextFlags.Password))
        {
            Config.YouTubeClientSecret = secret;
            Config.Save();
        }

        var flush = (int)Config.YouTubeFlush;
        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("Push when", ref flush, "The duty ends\0Manual only\0After every pull\0\0"))
        {
            Config.YouTubeFlush = (YouTubeFlushTrigger)flush;
            Config.Save();
        }

        ImGui.TextDisabled("Video id (blank = active live broadcast)");
        var vid = Config.YouTubeVideoId;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##ytvid", ref vid, 64))
        {
            Config.YouTubeVideoId = vid;
            Config.Save();
        }

        if (plugin.YouTube.IsConnected)
        {
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.55f, 0.9f), "Authorised");
            ImGui.SameLine();
            if (ImGui.Button("Disconnect##yt"))
                plugin.YouTube.Disconnect();
        }
        else if (plugin.YouTube.IsBusy)
        {
            ImGui.TextDisabled("Authorising (check your browser)...");
            ImGui.SameLine();
            if (ImGui.Button("Cancel##yt"))
                plugin.YouTube.CancelConnect();
        }
        else
        {
            ImGui.TextDisabled("Not authorised");
            ImGui.SameLine();
            if (ImGui.Button("Connect##yt"))
            {
                if (string.IsNullOrWhiteSpace(Config.YouTubeClientId) || string.IsNullOrWhiteSpace(Config.YouTubeClientSecret))
                    ImGui.OpenPopup(YtMissingPopup);
                else
                    ImGui.OpenPopup(YtConfirmPopup);
            }
        }

        if (!string.IsNullOrEmpty(plugin.YouTube.LastError))
            ImGui.TextColored(WarnColor, $"Last error: {plugin.YouTube.LastError}");

        DrawYouTubeConfirmPopup();
        DrawYouTubeMissingPopup();
    }

    private void DrawYouTubeMissingPopup()
    {
        var open = true;
        if (!ImGui.BeginPopupModal(YtMissingPopup, ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextWrapped("Enter your OAuth client id and secret above first. See docs/youtube-setup.md for how to create them.");
        ImGui.Spacing();
        if (ImGui.Button("OK", new Vector2(120, 0)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void DrawYouTubeConfirmPopup()
    {
        var open = true;
        if (!ImGui.BeginPopupModal(YtConfirmPopup, ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextWrapped(
            "This opens a browser to sign in to YouTube and grants this plugin permission to " +
            "manage your channel (edit video descriptions). The access token is saved on this " +
            "computer only. You can revoke it any time at myaccount.google.com under " +
            "'Third-party access'.");
        ImGui.Spacing();
        ImGui.TextColored(WarnColor, "Only continue if you set up your own OAuth client and trust this machine.");
        ImGui.Spacing();

        if (ImGui.Button("Continue", new Vector2(120, 0)))
        {
            ImGui.CloseCurrentPopup();
            _ = plugin.YouTube.ConnectAsync();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private static void DrawSectionHeader(string title)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextColored(HeaderColor, title);
        ImGui.Spacing();
    }

    public void Dispose() { }
}
