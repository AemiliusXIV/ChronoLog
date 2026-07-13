// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using ChronoLog.Model;
using ChronoLog.Output;

namespace ChronoLog.YouTube;

/// <summary>
/// Opt-in YouTube push. Authorises with the user's own OAuth client (their Cloud project),
/// stores the refresh token locally, and rewrites a marked section of a video's description
/// with the chapter block. Targets the active live broadcast when no explicit video id is set.
///
/// Everything is best-effort and off the game thread; failures surface through LastError.
/// </summary>
public sealed class YouTubeSink : IDisposable
{
    private const string Marker = "==== Pull timestamps ====";

    private readonly Configuration config;

    private YouTubeService? service;
    private CancellationTokenSource? connectCts;

    public bool IsConnected => service != null;

    // Claimed via Interlocked: FlushAsync is called fire-and-forget from several
    // places, so a plain check-then-set could let two flushes run at once.
    private int busy;
    public bool IsBusy => Volatile.Read(ref busy) == 1;

    public string? LastError { get; private set; }

    public bool HasStoredToken => Directory.Exists(TokenDir) && Directory.GetFiles(TokenDir).Length > 0;

    public YouTubeSink(Configuration config)
    {
        this.config = config;
    }

    private string TokenDir => Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "youtube-token");

    public void CancelConnect() => connectCts?.Cancel();

    public async Task ConnectAsync()
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        LastError = null;
        connectCts = new CancellationTokenSource();
        try
        {
            if (string.IsNullOrWhiteSpace(config.YouTubeClientId) || string.IsNullOrWhiteSpace(config.YouTubeClientSecret))
            {
                LastError = "Set your OAuth client id and secret first.";
                return;
            }

            var secrets = new ClientSecrets
            {
                ClientId = config.YouTubeClientId.Trim(),
                ClientSecret = config.YouTubeClientSecret.Trim(),
            };

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                new[] { YouTubeService.Scope.Youtube },
                "user",
                connectCts.Token,
                new FileDataStore(TokenDir, true));

            service = new YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "ChronoLog",
            });
        }
        catch (OperationCanceledException)
        {
            // User cancelled - nothing to report
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Plugin.Log.Warning(ex, "YouTube connect failed");
        }
        finally
        {
            Interlocked.Exchange(ref busy, 0);
            connectCts?.Dispose();
            connectCts = null;
        }
    }

    public void Disconnect()
    {
        service?.Dispose();
        service = null;
        try
        {
            if (Directory.Exists(TokenDir))
                Directory.Delete(TokenDir, true);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Could not clear YouTube token store");
        }
    }

    public async Task<bool> FlushAsync(RaidSession session)
    {
        if (service == null || session.Pulls.Count == 0)
            return false;
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            return false;

        LastError = null;
        try
        {
            var videoId = await ResolveVideoIdAsync();
            if (string.IsNullOrEmpty(videoId))
            {
                LastError = "No target video. Set a video id, or start a live broadcast.";
                return false;
            }

            var listReq = service.Videos.List("snippet");
            listReq.Id = videoId;
            var listResp = await listReq.ExecuteAsync();
            var video = listResp.Items?.FirstOrDefault();
            if (video?.Snippet == null)
            {
                LastError = $"Video {videoId} not found on the authorised channel.";
                return false;
            }

            var block = TextExporter.Build(session, config.TemplateFormat, config.EffectiveTimestampOffset(), config.PhaseTimestampsEnabled, config.PhaseTimestampOffsetSeconds);
            video.Snippet.Description = MergeDescription(video.Snippet.Description ?? string.Empty, block);

            var updateReq = service.Videos.Update(video, "snippet");
            await updateReq.ExecuteAsync();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Plugin.Log.Warning(ex, "YouTube flush failed");
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref busy, 0);
        }
    }

    private async Task<string?> ResolveVideoIdAsync()
    {
        if (!string.IsNullOrWhiteSpace(config.YouTubeVideoId))
            return config.YouTubeVideoId.Trim();

        if (service == null)
            return null;

        // mine=true and broadcastStatus are mutually exclusive in the current API.
        // broadcastStatus alone scopes to the authenticated user's broadcasts.
        var req = service.LiveBroadcasts.List("id");
        req.BroadcastStatus = LiveBroadcastsResource.ListRequest.BroadcastStatusEnum.Active;
        var resp = await req.ExecuteAsync();
        return resp.Items?.FirstOrDefault()?.Id;
    }

    /// <summary>Replaces the managed block (from the marker onward) and leaves the rest intact.</summary>
    private static string MergeDescription(string existing, string block)
    {
        var managed = Marker + "\n" + block;
        var idx = existing.IndexOf(Marker, StringComparison.Ordinal);
        if (idx >= 0)
            return existing.Substring(0, idx).TrimEnd() is var head && head.Length > 0
                ? head + "\n\n" + managed
                : managed;

        return existing.TrimEnd().Length > 0
            ? existing.TrimEnd() + "\n\n" + managed
            : managed;
    }

    public void Dispose()
    {
        connectCts?.Cancel();
        connectCts?.Dispose();
        connectCts = null;
        service?.Dispose();
        service = null;
    }
}
