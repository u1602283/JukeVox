using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using JukeVox.Server.Hubs;
using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public class PlaybackMonitorService : BackgroundService, IPlaybackMonitorService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PlaybackMonitorService> _logger;
    private readonly ConcurrentDictionary<string, PartyPlaybackState> _partyStates = new();

    public PlaybackMonitorService(
        IServiceProvider serviceProvider,
        ILogger<PlaybackMonitorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public PlaybackStateDto? GetCachedPlaybackState(string partyId)
    {
        if (!_partyStates.TryGetValue(partyId, out var ps))
            return null;

        var state = ps.CachedPlaybackState;
        if (state == null) return null;

        if (!state.IsPlaying || state.DurationMs <= 0) return state;

        var elapsedMs = (int)((DateTime.UtcNow.Ticks - ps.CachedAtTicks) / TimeSpan.TicksPerMillisecond);
        if (elapsedMs <= 0) return state;

        return new PlaybackStateDto
        {
            IsPlaying = state.IsPlaying,
            TrackUri = state.TrackUri,
            TrackName = state.TrackName,
            ArtistName = state.ArtistName,
            AlbumName = state.AlbumName,
            AlbumImageUrl = state.AlbumImageUrl,
            ProgressMs = Math.Min(state.ProgressMs + elapsedMs, state.DurationMs),
            DurationMs = state.DurationMs,
            VolumePercent = state.VolumePercent,
            SupportsVolume = state.SupportsVolume,
            DeviceId = state.DeviceId,
            DeviceName = state.DeviceName,
            AddedByName = state.AddedByName,
            IsFromBasePlaylist = state.IsFromBasePlaylist
        };
    }

    public void NotifyTrackStarted(string partyId, string trackUri)
    {
        var ps = GetOrCreateState(partyId);
        ps.LastTrackUri = trackUri;
        ps.WeStartedCurrentTrack = true;
        ps.IdleWatching = false;
        ps.WasPlaying = true;
        ps.TrackStartedAt = DateTime.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Playback monitor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllPartiesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in playback monitor");
            }

            await Task.Delay(2000, stoppingToken);
        }
    }

    private async Task PollAllPartiesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();

        var parties = partyService.GetAllParties();

        // Clean up state for parties that no longer exist
        foreach (var partyId in _partyStates.Keys)
        {
            if (!parties.Any(p => p.Id == partyId))
                _partyStates.TryRemove(partyId, out _);
        }

        foreach (var party in parties)
        {
            if (party.SpotifyTokens == null) continue;

            try
            {
                await PollPartyPlaybackAsync(party.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling playback for party {PartyId}", party.Id);
            }
        }
    }

    private async Task PollPartyPlaybackAsync(string partyId, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();

        // Set party context so Spotify services resolve the correct tokens
        var accessor = scope.ServiceProvider.GetRequiredService<IPartyContextAccessor>();
        accessor.PartyId = partyId;

        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var playerService = scope.ServiceProvider.GetRequiredService<ISpotifyPlayerService>();
        var queueService = scope.ServiceProvider.GetRequiredService<IQueueService>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<PartyHub, IPartyClient>>();

        var party = partyService.GetParty(partyId);
        if (party?.SpotifyTokens == null) return;

        var ps = GetOrCreateState(partyId);
        var state = await playerService.GetPlaybackStateAsync();

        bool shouldAdvance = false;

        if (state == null || state.TrackName == null)
        {
            if (ps.WasPlaying && !ps.IdleWatching)
            {
                _logger.LogInformation("[{PartyId}] Playback stopped, advancing queue", partyId);
                shouldAdvance = true;
            }
        }
        else
        {
            if (state.DeviceId != null)
                ps.LastDeviceId = state.DeviceId;

            if (!shouldAdvance && ps.WasPlaying && !state.IsPlaying && state.TrackUri == ps.LastTrackUri)
            {
                bool prevWasNearEnd = ps.LastDurationMs > 0 &&
                                      ps.LastProgressMs >= ps.LastDurationMs - 5000;
                bool progressReset = state.ProgressMs < ps.LastProgressMs - 5000;

                if (prevWasNearEnd || progressReset)
                {
                    _logger.LogInformation("[{PartyId}] Track finished, advancing queue", partyId);
                    shouldAdvance = true;
                }
            }

            var inGracePeriod = (DateTime.UtcNow - ps.TrackStartedAt).TotalSeconds < 5;

            if (!shouldAdvance && !inGracePeriod &&
                (ps.WeStartedCurrentTrack || ps.IdleWatching) && ps.LastTrackUri != null &&
                state.TrackUri != null && state.TrackUri != ps.LastTrackUri &&
                state.IsPlaying)
            {
                var peekQueue = queueService.GetQueue(partyId);
                if (peekQueue.Count > 0)
                {
                    _logger.LogInformation("[{PartyId}] Spotify auto-advanced to foreign track, taking over", partyId);
                    shouldAdvance = true;
                }
                else
                {
                    ps.WeStartedCurrentTrack = false;
                }
            }

            if (!shouldAdvance && ps.IdleWatching && !ps.WeStartedCurrentTrack && state != null)
            {
                var appQueue = queueService.GetQueue(partyId);
                if (appQueue.Count > 0 && !state.IsPlaying)
                {
                    _logger.LogInformation("[{PartyId}] Idle watching: advancing", partyId);
                    shouldAdvance = true;
                }
            }

            if (!shouldAdvance)
            {
                bool staleDuringGrace = inGracePeriod && state!.TrackUri != ps.LastTrackUri;
                if (!staleDuringGrace)
                {
                    if (party.CurrentTrack != null)
                    {
                        state!.AddedByName = party.CurrentTrack.AddedByName;
                        state.IsFromBasePlaylist = party.CurrentTrack.IsFromBasePlaylist;
                    }

                    ps.CachedPlaybackState = state;
                    ps.CachedAtTicks = DateTime.UtcNow.Ticks;

                    if (state!.TrackUri != ps.LastTrackUri)
                    {
                        await hubContext.Clients.Group(party.Id).NowPlayingChanged(state);
                    }

                    await hubContext.Clients.Group(party.Id).PlaybackStateUpdated(state);
                }
            }

            if (inGracePeriod && state!.TrackUri == ps.LastTrackUri)
                ps.TrackStartedAt = DateTime.MinValue;

            ps.WasPlaying = state!.IsPlaying;
            ps.LastProgressMs = state.ProgressMs;
            ps.LastDurationMs = state.DurationMs;
            ps.LastTrackUri = state.TrackUri;
        }

        if (shouldAdvance)
        {
            ps.WasPlaying = false;
            var next = queueService.Dequeue(partyId);
            if (next != null)
            {
                var played = await PlayWithDeviceFallback(playerService, next.TrackUri, ps);
                if (played)
                {
                    _logger.LogInformation("[{PartyId}] Now playing: {Track}", partyId, next.TrackName);
                    ps.WeStartedCurrentTrack = true;
                    ps.IdleWatching = false;
                    ps.LastTrackUri = next.TrackUri;
                    ps.WasPlaying = true;

                    var nowPlayingDto = new PlaybackStateDto
                    {
                        IsPlaying = true,
                        TrackUri = next.TrackUri,
                        TrackName = next.TrackName,
                        ArtistName = next.ArtistName,
                        AlbumName = next.AlbumName,
                        AlbumImageUrl = next.AlbumImageUrl,
                        ProgressMs = 0,
                        DurationMs = next.DurationMs,
                        VolumePercent = state?.VolumePercent ?? 0,
                        SupportsVolume = state?.SupportsVolume ?? true,
                        DeviceId = state?.DeviceId ?? ps.LastDeviceId,
                        DeviceName = state?.DeviceName,
                        AddedByName = next.AddedByName,
                        IsFromBasePlaylist = next.IsFromBasePlaylist
                    };
                    ps.CachedPlaybackState = nowPlayingDto;
                    ps.CachedAtTicks = DateTime.UtcNow.Ticks;
                    await hubContext.Clients.Group(party.Id).NowPlayingChanged(nowPlayingDto);
                }
                else
                {
                    _logger.LogWarning("[{PartyId}] Failed to play track: {Track}", partyId, next.TrackName);
                }

                var queue = queueService.GetQueue(partyId);
                await hubContext.Clients.Group(party.Id).QueueUpdated(queue);
            }
            else
            {
                ps.WeStartedCurrentTrack = false;
                ps.IdleWatching = true;
                ps.CachedPlaybackState = null;
                _logger.LogInformation("[{PartyId}] Queue empty, entering idle watching mode", partyId);
            }
        }
    }

    private PartyPlaybackState GetOrCreateState(string partyId)
    {
        return _partyStates.GetOrAdd(partyId, _ => new PartyPlaybackState());
    }

    private static async Task<bool> PlayWithDeviceFallback(ISpotifyPlayerService playerService, string trackUri, PartyPlaybackState ps)
    {
        if (await playerService.PlayTrackAsync(trackUri, ps.LastDeviceId))
            return true;

        var devices = await playerService.GetDevicesAsync();
        if (devices.Count == 0)
            return false;

        var target = devices.FirstOrDefault(d => d.Id == ps.LastDeviceId)
                     ?? devices.FirstOrDefault(d => d.IsActive)
                     ?? devices[0];

        ps.LastDeviceId = target.Id;
        return await playerService.PlayTrackAsync(trackUri, target.Id);
    }

    private class PartyPlaybackState
    {
        public string? LastTrackUri;
        public string? LastDeviceId;
        public bool WasPlaying;
        public int LastProgressMs;
        public int LastDurationMs;
        public bool WeStartedCurrentTrack;
        public bool IdleWatching;
        public DateTime TrackStartedAt = DateTime.MinValue;
        public volatile PlaybackStateDto? CachedPlaybackState;
        public long CachedAtTicks;
    }
}
