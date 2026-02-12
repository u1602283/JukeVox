using Microsoft.AspNetCore.SignalR;
using JukeVox.Server.Hubs;
using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public class PlaybackMonitorService : BackgroundService, IPlaybackMonitorService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PlaybackMonitorService> _logger;

    private string? _lastTrackUri;
    private string? _lastDeviceId;
    private bool _wasPlaying;
    private int _lastProgressMs;
    private int _lastDurationMs;
    private bool _weStartedCurrentTrack;
    private bool _idleWatching;
    private DateTime _trackStartedAt = DateTime.MinValue;
    private volatile PlaybackStateDto? _cachedPlaybackState;
    private long _cachedAtTicks;

    public PlaybackMonitorService(
        IServiceProvider serviceProvider,
        ILogger<PlaybackMonitorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public PlaybackStateDto? GetCachedPlaybackState()
    {
        var state = _cachedPlaybackState;
        if (state == null) return null;

        if (!state.IsPlaying || state.DurationMs <= 0) return state;

        var elapsedMs = (int)((DateTime.UtcNow.Ticks - _cachedAtTicks) / TimeSpan.TicksPerMillisecond);
        if (elapsedMs <= 0) return state;

        // Return a copy with interpolated progress (don't mutate the cached instance)
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

    /// <summary>
    /// Called by PlaybackController when a track is played via the in-app skip button.
    /// Updates internal state so the monitor doesn't misinterpret the track change.
    /// </summary>
    public void NotifyTrackStarted(string trackUri)
    {
        _lastTrackUri = trackUri;
        _weStartedCurrentTrack = true;
        _idleWatching = false;
        _wasPlaying = true;
        _trackStartedAt = DateTime.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Playback monitor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollPlaybackAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in playback monitor");
            }

            await Task.Delay(2000, stoppingToken);
        }
    }

    private async Task PollPlaybackAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var playerService = scope.ServiceProvider.GetRequiredService<ISpotifyPlayerService>();
        var queueService = scope.ServiceProvider.GetRequiredService<IQueueService>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<PartyHub, IPartyClient>>();

        var party = partyService.GetCurrentParty();
        if (party?.SpotifyTokens == null) return;

        var state = await playerService.GetPlaybackStateAsync();

        bool shouldAdvance = false;

        if (state == null || state.TrackName == null)
        {
            // Spotify returned nothing — if we were playing, the track ended
            if (_wasPlaying && !_idleWatching)
            {
                _logger.LogInformation("Playback stopped (no state from Spotify), advancing queue");
                shouldAdvance = true;
            }
            // If idle watching and Spotify disappears, just stay idle — device went to sleep
        }
        else
        {
            // Remember the last active device so we can target it later
            if (state.DeviceId != null)
                _lastDeviceId = state.DeviceId;

            // Detect track ended: was playing, now stopped on the same track.
            // Spotify resets progress to 0 when a track finishes, so we check
            // whether the *previous* poll's progress was near the end.
            if (!shouldAdvance && _wasPlaying && !state.IsPlaying && state.TrackUri == _lastTrackUri)
            {
                bool prevWasNearEnd = _lastDurationMs > 0 &&
                                      _lastProgressMs >= _lastDurationMs - 5000;
                bool progressReset = state.ProgressMs < _lastProgressMs - 5000;

                if (prevWasNearEnd || progressReset)
                {
                    _logger.LogInformation(
                        "Track finished (prev progress {PrevProgress}/{Duration}, now {Progress}), advancing queue",
                        _lastProgressMs, _lastDurationMs, state.ProgressMs);
                    shouldAdvance = true;
                }
            }

            // Grace period: after a controller-initiated track change, Spotify may
            // briefly report the old track. Skip foreign-track detection during this window.
            var inGracePeriod = (DateTime.UtcNow - _trackStartedAt).TotalSeconds < 5;

            // Detect Spotify moved to a different track (skip, auto-advance, etc.)
            if (!shouldAdvance && !inGracePeriod &&
                (_weStartedCurrentTrack || _idleWatching) && _lastTrackUri != null &&
                state.TrackUri != null && state.TrackUri != _lastTrackUri &&
                state.IsPlaying)
            {
                // Foreign track — take over if we have queue items
                var peekQueue = queueService.GetQueue();
                if (peekQueue.Count > 0)
                {
                    _logger.LogInformation("Spotify auto-advanced to foreign track, taking over");
                    shouldAdvance = true;
                }
                else
                {
                    _weStartedCurrentTrack = false;
                }
            }

            // Idle re-engagement: queue has items and Spotify stopped/paused
            if (!shouldAdvance && _idleWatching && !_weStartedCurrentTrack && state != null)
            {
                var appQueue = queueService.GetQueue();
                if (appQueue.Count > 0 && !state.IsPlaying)
                {
                    _logger.LogInformation("Idle watching: queue has items and Spotify idle, advancing");
                    shouldAdvance = true;
                }
            }

            // Only broadcast state when we're NOT about to advance —
            // otherwise clients briefly see the old track reset to 0.
            // Also skip broadcasting during grace period if Spotify still reports
            // the old track — the controller already sent the correct state.
            if (!shouldAdvance)
            {
                bool staleDuringGrace = inGracePeriod && state!.TrackUri != _lastTrackUri;
                if (!staleDuringGrace)
                {
                    // Enrich with current track attribution
                    if (party.CurrentTrack != null)
                    {
                        state!.AddedByName = party.CurrentTrack.AddedByName;
                        state!.IsFromBasePlaylist = party.CurrentTrack.IsFromBasePlaylist;
                    }

                    _cachedPlaybackState = state;
                    _cachedAtTicks = DateTime.UtcNow.Ticks;

                    if (state!.TrackUri != _lastTrackUri)
                    {
                        await hubContext.Clients.Group(party.Id).NowPlayingChanged(state);
                    }

                    await hubContext.Clients.Group(party.Id).PlaybackStateUpdated(state);
                }
            }

            // Clear grace period once Spotify reports the expected track
            if (inGracePeriod && state!.TrackUri == _lastTrackUri)
                _trackStartedAt = DateTime.MinValue;

            _wasPlaying = state!.IsPlaying;
            _lastProgressMs = state.ProgressMs;
            _lastDurationMs = state.DurationMs;
            _lastTrackUri = state.TrackUri;
        }

        if (shouldAdvance)
        {
            _wasPlaying = false;
            var next = queueService.Dequeue();
            if (next != null)
            {
                var played = await PlayWithDeviceFallback(playerService, next.TrackUri);
                if (played)
                {
                    _logger.LogInformation("Now playing from queue: {Track}", next.TrackName);
                    _weStartedCurrentTrack = true;
                    _idleWatching = false;
                    _lastTrackUri = next.TrackUri;
                    _wasPlaying = true;

                    // Immediately tell clients about the new track so the UI
                    // transitions instantly instead of waiting for the next poll
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
                        DeviceId = state?.DeviceId ?? _lastDeviceId,
                        DeviceName = state?.DeviceName,
                        AddedByName = next.AddedByName,
                        IsFromBasePlaylist = next.IsFromBasePlaylist
                    };
                    _cachedPlaybackState = nowPlayingDto;
                    _cachedAtTicks = DateTime.UtcNow.Ticks;
                    await hubContext.Clients.Group(party.Id).NowPlayingChanged(nowPlayingDto);

                }
                else
                {
                    _logger.LogWarning("Failed to play track: {Track}", next.TrackName);
                }

                var queue = queueService.GetQueue();
                await hubContext.Clients.Group(party.Id).QueueUpdated(queue);
            }
            else
            {
                _weStartedCurrentTrack = false;
                _idleWatching = true;
                _cachedPlaybackState = null;
                _logger.LogInformation("Queue empty, entering idle watching mode");
            }
        }
    }

    /// <summary>
    /// Attempts to play a track, targeting the last known device.
    /// If that fails (device went inactive), fetches the device list and retries
    /// on the first available device.
    /// </summary>
    private async Task<bool> PlayWithDeviceFallback(ISpotifyPlayerService playerService, string trackUri)
    {
        // First attempt: target the last known device
        if (await playerService.PlayTrackAsync(trackUri, _lastDeviceId))
            return true;

        _logger.LogWarning("Play failed on last device {DeviceId}, searching for available devices", _lastDeviceId);

        // Fallback: find any available device
        var devices = await playerService.GetDevicesAsync();
        if (devices.Count == 0)
        {
            _logger.LogWarning("No Spotify devices available");
            return false;
        }

        // Prefer the previously active device, then any active one, then the first device
        var target = devices.FirstOrDefault(d => d.Id == _lastDeviceId)
                     ?? devices.FirstOrDefault(d => d.IsActive)
                     ?? devices[0];

        _logger.LogInformation("Retrying play on device: {DeviceName} ({DeviceId})", target.Name, target.Id);
        _lastDeviceId = target.Id;

        return await playerService.PlayTrackAsync(trackUri, target.Id);
    }
}
