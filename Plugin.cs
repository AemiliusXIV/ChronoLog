// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog

using System;
using Dalamud.Game.Command;
using Dalamud.Game.DutyState;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using ChronoLog.Capture;
using ChronoLog.Model;
using ChronoLog.Obs;
using ChronoLog.Output;
using ChronoLog.Phases;
using ChronoLog.Windows;
using ChronoLog.YouTube;

namespace ChronoLog;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "ChronoLog";

    private const string MainCommand = "/chrono";
    private const string AliasCommand = "/raidts";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;

    internal Configuration Config { get; }
    internal WindowSystem WindowSystem { get; } = new("ChronoLog");

    // OBS auto-reconnect state (all accessed on framework thread only, no locks needed).
    private DateTime? obsReconnectAt;       // when to fire the reconnect attempt
    private DateTime? obsReconnectCheckAt;  // deadline: if still not connected by here, give up
    private bool obsReconnectPending;       // true between scheduling and firing the attempt
    // Reconnects that start within this window of plugin load are silent: the disconnect is
    // almost certainly the old plugin instance's WebSocket closing during a reload/update,
    // not a real mid-session OBS failure.
    private readonly DateTime pluginLoadedAt = DateTime.UtcNow;
    private bool obsStartupSilentReconnect; // whether the current reconnect cycle should print nothing
    internal ConfigWindow ConfigWindow { get; }
    internal MainWindow MainWindow { get; }

    internal BossHpReader BossReader { get; }
    internal PhaseResolver Phases { get; }
    internal CombatEventCapture CombatCapture { get; }
    internal ObsWebSocketClient Obs { get; }
    internal ObsChapterSink ChapterSink { get; }
    internal MarkOverlayWindow MarkOverlay { get; }
    internal YouTubeSink YouTube { get; }
    internal SessionStore Store { get; }
    internal DutyTracker DutyTracker { get; }

    /// <summary>Live session if one is running, otherwise the most recent stored one.</summary>
    internal RaidSession? ActiveSession => DutyTracker.Session ?? Store.MostRecent;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(PluginInterface);

        BossReader = new BossHpReader();
        Phases = new PhaseResolver(Config);
        CombatCapture = new CombatEventCapture();
        CombatCapture.Enable();
        Obs = new ObsWebSocketClient();

        YouTube = new YouTubeSink(Config);
        if (Config.YouTubeEnabled && YouTube.HasStoredToken)
            _ = YouTube.ConnectAsync();
        Store = new SessionStore();

        DutyTracker = new DutyTracker(Config, BossReader, Phases, Store, Obs.GetOffset, CombatCapture.GetCause);
        DutyTracker.PullStarted += _ => CombatCapture.Clear();
        DutyTracker.PullCommitted += OnPullCommitted;
        DutyTracker.Cleared += OnCleared;
        CombatCapture.ActionObserved += DutyTracker.NoteBossAction;

        ChapterSink = new ObsChapterSink(Config, Obs, DutyTracker);

        if (Config.ObsEnabled)
            Obs.Connect(Config.ObsHost, Config.ObsPort, Config.ObsPassword);

        DutyState.DutyStarted += OnDutyStartedObsCheck;

        MarkOverlay = new MarkOverlayWindow(this);
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MarkOverlay);
        MarkOverlay.IsOpen = true;
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        PluginInterface.UiBuilder.OpenMainUi += OpenMain;
        Framework.Update += OnFrameworkUpdate;

        CommandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open ChronoLog. '/chrono cfg' opens settings.",
            ShowInHelp = true,
        });
        CommandManager.AddHandler(AliasCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /chrono.",
            ShowInHelp = false,
        });
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMain;

        DutyState.DutyStarted -= OnDutyStartedObsCheck;
        CommandManager.RemoveHandler(MainCommand);
        CommandManager.RemoveHandler(AliasCommand);
        WindowSystem.RemoveAllWindows();

        ChapterSink.Dispose();
        DutyTracker.Dispose();
        CombatCapture.Dispose();
        Obs.Dispose();
        YouTube.Dispose();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!Config.Enabled)
            return;

        try
        {
            DutyTracker.Tick();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DutyTracker.Tick threw");
        }

        HandleObsReconnect();
        HandleObsStreamRestart();
    }

    private void OnPullCommitted(PullEntry pull)
    {
        if (Config.YouTubeEnabled && Config.YouTubeFlush == YouTubeFlushTrigger.EveryPull)
        {
            var live = DutyTracker.Session;
            if (live != null)
                _ = YouTube.FlushAsync(live);
        }
    }

    /// <summary>
    /// Marks (or unmarks) the active pull, or the most recent committed pull if none is active.
    /// When MarkUsePressTime is on and a pull is in progress, the current stream offset is stored
    /// so the export chapter links to this moment rather than the pull start.
    /// </summary>
    /// <summary>
    /// Clears all committed pulls from a session and resets its attempt counter.
    /// Any pull currently in progress is left running; it becomes attempt 1 when it resolves.
    /// </summary>
    internal void ResetSession(Model.RaidSession session)
    {
        session.Pulls.Clear();
        session.AttemptCounter = 0;
        session.DiscardedCount = 0;
        // If a pull is already in progress its attempt number was stamped before the reset.
        // Renumber it to 1 so ResolvePull doesn't write the old number back to AttemptCounter.
        if (session.Current != null)
            session.Current.Attempt = 1;
        Store.Save();
    }

    internal void MarkCurrentOrLast()
    {
        var session = DutyTracker.Session;
        if (session == null) return;

        Model.PullEntry? target = session.Current;
        if (target == null && session.Pulls.Count > 0)
            target = session.Pulls[^1];
        if (target == null) return;

        target.IsMarked = !target.IsMarked;
        if (target.IsMarked)
        {
            if (Config.MarkUsePressTime && session.Current != null)
                target.MarkOffset = Obs.GetOffset();
            target.MarkUtc = DateTime.UtcNow;
        }
        else
        {
            target.MarkOffset = null;
            target.MarkUtc = null;
        }
    }

    // Runs every framework tick. Polls OBS output state and reacts to stream restarts.
    private void HandleObsStreamRestart()
    {
        Obs.PollStatus();

        if (!Obs.ConsumePendingStreamRestart())
            return;

        var session = DutyTracker.Session ?? Store.MostRecent;
        var hasPulls = session?.Pulls.Count > 0;

        switch (Config.StreamRestartBehavior)
        {
            case StreamRestartBehavior.NotifyOnly:
                ChatGui.Print("[ChronoLog] New OBS stream detected. Old timestamps are from a previous VOD - use Reset session in /chrono if you want a clean list.");
                break;

            case StreamRestartBehavior.AutoReset:
                if (session != null && hasPulls)
                {
                    ResetSession(session);
                    ChatGui.Print("[ChronoLog] New OBS stream detected. Session cleared automatically.");
                }
                break;
        }
    }

    // Runs every framework tick (game main thread). Handles unexpected OBS disconnects:
    // notifies the user, waits 5 s, makes one reconnect attempt, then reports the result.
    private void HandleObsReconnect()
    {
        if (!Config.ObsEnabled)
            return;

        var now = DateTime.UtcNow;

        // New unexpected disconnect from the websocket background thread.
        if (Obs.ConsumePendingDisconnect() && !obsReconnectPending && !obsReconnectAt.HasValue)
        {
            obsReconnectAt = now.AddSeconds(5);
            obsReconnectPending = true;
            // Disconnects within ~15 s of startup are almost always the previous plugin
            // instance's WebSocket closing during a reload or update - not a real mid-session
            // failure. Reconnect silently so the user doesn't see spurious noise.
            obsStartupSilentReconnect = (now - pluginLoadedAt).TotalSeconds < 15;
            if (!obsStartupSilentReconnect)
                ChatGui.Print("[ChronoLog] OBS connection lost. Reconnecting in 5 seconds...");
        }

        // Fire the scheduled reconnect attempt.
        if (obsReconnectAt.HasValue && now >= obsReconnectAt.Value && !Obs.IsConnected && !Obs.IsConnecting)
        {
            obsReconnectAt = null;
            obsReconnectCheckAt = now.AddSeconds(12);
            Obs.Connect(Config.ObsHost, Config.ObsPort, Config.ObsPassword);
        }
        else if (obsReconnectAt.HasValue && now >= obsReconnectAt.Value && Obs.IsConnected)
        {
            // Already reconnected on its own (unlikely but clean to handle).
            obsReconnectAt = null;
            obsReconnectPending = false;
        }

        // Check window: see if the reconnect attempt worked or failed.
        if (obsReconnectCheckAt.HasValue && now >= obsReconnectCheckAt.Value)
        {
            obsReconnectCheckAt = null;
            obsReconnectPending = false;
            Obs.ConsumePendingDisconnect(); // discard any noise from the failed attempt
            if (!obsStartupSilentReconnect)
            {
                if (Obs.IsConnected)
                    ChatGui.Print("[ChronoLog] OBS reconnected.");
                else
                    ChatGui.Print("[ChronoLog] OBS reconnect failed. Reconnect manually in /chrono cfg.");
            }
            obsStartupSilentReconnect = false;
        }

        // Reconnect succeeded before the check deadline.
        if (obsReconnectPending && obsReconnectCheckAt.HasValue && Obs.IsConnected)
        {
            obsReconnectCheckAt = null;
            obsReconnectPending = false;
            if (!obsStartupSilentReconnect)
                ChatGui.Print("[ChronoLog] OBS reconnected.");
            obsStartupSilentReconnect = false;
        }
    }

    private void OnDutyStartedObsCheck(IDutyStateEventArgs args)
    {
        if (!Config.ObsEnabled || !Config.WarnOnObsDisconnected)
            return;

        if (!Obs.IsConnected)
            ChatGui.Print("[ChronoLog] OBS is not connected - chapter markers will be missed. Connect via /chrono cfg.");
        else if (Obs.OutputActive == false)
            ChatGui.Print("[ChronoLog] OBS is connected but not recording or streaming. Start a recording in OBS before you pull.");
    }

    private void OnCleared(RaidSession session)
    {
        if (session.Pulls.Count == 0)
            return;

        if (Config.AutoExportOnClear)
        {
            try
            {
                var content = TextExporter.Build(session, Config.TemplateFormat, Config.EffectiveTimestampOffset(), Config.PhaseTimestampsEnabled, Config.PhaseTimestampOffsetSeconds);
                if (!string.IsNullOrEmpty(content))
                {
                    var path = TextExporter.WriteToFile(ExportDirectory(), session, content);
                    Log.Information($"ChronoLog: wrote description block to {path}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Auto-export failed");
            }
        }

        if (Config.YouTubeEnabled && Config.YouTubeFlush == YouTubeFlushTrigger.OnClear)
            _ = YouTube.FlushAsync(session);
    }

    /// <summary>Manual YouTube push of the active (or last) session, used by the UI button.</summary>
    internal void FlushYouTubeNow()
    {
        var session = ActiveSession;
        if (session != null)
            _ = YouTube.FlushAsync(session);
    }

    /// <summary>Resolved export folder: the configured one, or Documents\ChronoLog.</summary>
    internal string ExportDirectory()
    {
        var dir = Config.TextExportDirectory;
        if (!string.IsNullOrWhiteSpace(dir))
            return dir;
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return System.IO.Path.Combine(docs, "ChronoLog");
    }

    internal void OpenExportFolder()
    {
        try
        {
            var dir = ExportDirectory();
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Open export folder failed");
        }
    }

    internal void OpenSettings() => ConfigWindow.IsOpen = true;
    internal void OpenMain() => MainWindow.IsOpen = true;

    private void OnCommand(string command, string args)
    {
        var trimmed = (args ?? string.Empty).Trim().ToLowerInvariant();
        if (trimmed is "cfg" or "config" or "settings")
            OpenSettings();
        else if (trimmed == "mark")
            MarkCurrentOrLast();
        else
            OpenMain();
    }

    /// <summary>
    /// ContentFinderCondition name for a territory id, or null for open-world / unknown.
    /// Pulled from game data so it needs no plugin update as content rotates in.
    /// </summary>
    internal static string? GetDutyName(uint territoryId)
    {
        if (territoryId == 0) return null;
        try
        {
            var sheet = DataManager.GetExcelSheet<ContentFinderCondition>();
            if (sheet == null) return null;
            foreach (var row in sheet)
            {
                if (row.TerritoryType.RowId == territoryId)
                {
                    var name = row.Name.ToString();
                    return string.IsNullOrEmpty(name) ? null : name;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GetDutyName failed for territory {0}", territoryId);
        }
        return null;
    }
}
