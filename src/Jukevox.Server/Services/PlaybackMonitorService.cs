using Microsoft.AspNetCore.SignalR;
using JukeVox.Server.Hubs;
using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public class PlaybackMonitorService : BackgroundService
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
    private bool _spotifyQueueHasForeignItems;
    private int _pollCount;
    private string? _seededTrackUri;
    private DateTime _trackStartedAt = DateTime.MinValue;

    public PlaybackMonitorService(
        IServiceProvider serviceProvider,
        ILogger<PlaybackMonitorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
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
        _seededTrackUri = null;
        _spotifyQueueHasForeignItems = false;
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
        var partyService = scope.ServiceProvider.GetRequiredService<PartyService>();
        var playerService = scope.ServiceProvider.GetRequiredService<SpotifyPlayerService>();
        var queueService = scope.ServiceProvider.GetRequiredService<QueueService>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<PartyHub, IPartyClient>>();

        var party = partyService.GetCurrentParty();
        if (party?.SpotifyTokens == null) return;

        var state = await playerService.GetPlaybackStateAsync();

        _pollCount++;

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

            // Periodically check Spotify's native queue for foreign items (~every 10s)
            if (_pollCount % 5 == 0 && _weStartedCurrentTrack && state.IsPlaying)
            {
                var appQueue = queueService.GetQueue();
                if (appQueue.Count > 0)
                {
                    var spotifyQueue = await playerService.GetSpotifyQueueAsync();
                    var nextAppTrackUri = appQueue[0].TrackUri;
                    // Foreign items = anything in Spotify's queue ahead of our next track
                    _spotifyQueueHasForeignItems = spotifyQueue.Count > 0 &&
                                                    spotifyQueue[0] != nextAppTrackUri;
                    if (_spotifyQueueHasForeignItems)
                        _logger.LogInformation("Spotify queue has foreign items (next: {SpotifyNext}, expected: {AppNext})",
                            spotifyQueue[0], nextAppTrackUri);
                }
                else
                {
                    _spotifyQueueHasForeignItems = false;
                }
            }

            // Preemptive advance: if we're near the end of the track and Spotify's
            // queue has foreign items, play our next track now to cut them off
            if (_weStartedCurrentTrack && state.IsPlaying && _spotifyQueueHasForeignItems &&
                state.DurationMs > 0 && state.ProgressMs >= state.DurationMs - 3000)
            {
                _logger.LogInformation("Track near end with foreign Spotify queue — preemptively advancing");
                shouldAdvance = true;
                _spotifyQueueHasForeignItems = false;
            }

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
                // Check if Spotify landed on our seeded track (e.g. user hit skip)
                if (_seededTrackUri != null && state.TrackUri == _seededTrackUri)
                {
                    _logger.LogInformation("Skip detected — Spotify playing our seeded track");
                    // Dequeue the seeded track from our app queue (it's now playing)
                    var skippedTo = queueService.Dequeue();
                    _weStartedCurrentTrack = true;
                    _lastTrackUri = state.TrackUri;
                    _seededTrackUri = null;
                    _spotifyQueueHasForeignItems = false;

                    // Notify clients of the new now-playing and updated queue
                    await hubContext.Clients.Group(party.Id).NowPlayingChanged(state);
                    var updatedQueue = queueService.GetQueue();
                    await hubContext.Clients.Group(party.Id).QueueUpdated(updatedQueue);

                    // Seed the next track from our queue
                    await SeedNextTrack(playerService, queueService);
                }
                else
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
                        _seededTrackUri = null;
                    }
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
                        DeviceName = state?.DeviceName
                    };
                    await hubContext.Clients.Group(party.Id).NowPlayingChanged(nowPlayingDto);

                    // Seed next track into Spotify's native queue for skip resilience
                    await SeedNextTrack(playerService, queueService);
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
                _logger.LogInformation("Queue empty, entering idle watching mode");
            }
        }
    }

    /// <summary>
    /// Seeds the next track from our app queue into Spotify's native queue,
    /// so that hardware/app skips land on our track instead of Spotify autoplay.
    /// </summary>
    private async Task SeedNextTrack(SpotifyPlayerService playerService, QueueService queueService)
    {
        var appQueue = queueService.GetQueue();
        if (appQueue.Count > 0)
        {
            var nextUri = appQueue[0].TrackUri;
            var seeded = await playerService.AddToQueueAsync(nextUri);
            if (seeded)
            {
                _seededTrackUri = nextUri;
                _logger.LogInformation("Seeded next track into Spotify queue: {Uri}", nextUri);
            }
            else
            {
                _seededTrackUri = null;
                _logger.LogWarning("Failed to seed next track into Spotify queue");
            }
        }
        else
        {
            _seededTrackUri = null;
        }
    }

    /// <summary>
    /// Attempts to play a track, targeting the last known device.
    /// If that fails (device went inactive), fetches the device list and retries
    /// on the first available device.
    /// </summary>
    private async Task<bool> PlayWithDeviceFallback(SpotifyPlayerService playerService, string trackUri)
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
