using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using JukeVox.Server.Extensions;
using JukeVox.Server.Hubs;
using JukeVox.Server.Middleware;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Services;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/playback")]
public class PlaybackController : ControllerBase
{
    private readonly ISpotifyPlayerService _playerService;
    private readonly IPartyService _partyService;
    private readonly IQueueService _queueService;
    private readonly IPlaybackMonitorService _monitorService;
    private readonly IHubContext<PartyHub, IPartyClient> _hubContext;

    public PlaybackController(
        ISpotifyPlayerService playerService,
        IPartyService partyService,
        IQueueService queueService,
        IPlaybackMonitorService monitorService,
        IHubContext<PartyHub, IPartyClient> hubContext)
    {
        _playerService = playerService;
        _partyService = partyService;
        _queueService = queueService;
        _monitorService = monitorService;
        _hubContext = hubContext;
    }

    private string? GetHostPartyId()
    {
        if (!HttpContext.IsHostAuthenticated()) return null;
        var sessionId = HttpContext.GetSessionId();
        return _partyService.GetPartyIdForSession(sessionId);
    }

    [HttpPost("pause")]
    public async Task<IActionResult> Pause()
    {
        var partyId = GetHostPartyId();
        if (partyId == null) return Forbid();

        var success = await _playerService.PauseAsync();
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpPost("resume")]
    public async Task<IActionResult> Resume()
    {
        var partyId = GetHostPartyId();
        if (partyId == null) return Forbid();

        var success = await _playerService.ResumeAsync();
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpPost("previous")]
    public async Task<IActionResult> Previous([FromQuery] int progressMs = 0)
    {
        var partyId = GetHostPartyId();
        if (partyId == null) return Forbid();

        if (progressMs > 5000)
        {
            var success = await _playerService.SeekAsync(0);
            return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
        }

        var prev = _queueService.SkipToPrevious(partyId);
        if (prev != null)
        {
            await _playerService.PlayTrackAsync(prev.TrackUri);
            _monitorService.NotifyTrackStarted(partyId, prev.TrackUri);

            await BroadcastNowPlaying(partyId, prev);

            var queue = _queueService.GetQueue(partyId);
            await _hubContext.Clients.Group(partyId).QueueUpdated(queue);
            return Ok();
        }

        var success2 = await _playerService.SeekAsync(0);
        return success2 ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpPost("skip")]
    public async Task<IActionResult> Skip()
    {
        var partyId = GetHostPartyId();
        if (partyId == null) return Forbid();

        var next = _queueService.Dequeue(partyId);

        if (next != null)
        {
            await _playerService.PlayTrackAsync(next.TrackUri);
            _monitorService.NotifyTrackStarted(partyId, next.TrackUri);

            await BroadcastNowPlaying(partyId, next);

            var upcoming = _queueService.GetQueue(partyId);
            await _hubContext.Clients.Group(partyId).QueueUpdated(upcoming);
        }
        else
        {
            await _playerService.SkipNextAsync();
        }

        return Ok();
    }

    [HttpPut("seek")]
    public async Task<IActionResult> Seek([FromQuery] int positionMs)
    {
        var partyId = GetHostPartyId();
        if (partyId == null) return Forbid();

        var success = await _playerService.SeekAsync(Math.Max(positionMs, 0));
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpPut("volume")]
    public async Task<IActionResult> SetVolume([FromQuery] int percent)
    {
        var partyId = GetHostPartyId();
        if (partyId == null) return Forbid();

        var success = await _playerService.SetVolumeAsync(Math.Clamp(percent, 0, 100));
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices()
    {
        var partyId = GetHostPartyId();
        if (partyId == null) return Forbid();

        var devices = await _playerService.GetDevicesAsync();
        return Ok(devices);
    }

    private async Task BroadcastNowPlaying(string partyId, Models.QueueItem track)
    {
        var cached = _monitorService.GetCachedPlaybackState(partyId);
        var dto = new PlaybackStateDto
        {
            IsPlaying = true,
            TrackUri = track.TrackUri,
            TrackName = track.TrackName,
            ArtistName = track.ArtistName,
            AlbumName = track.AlbumName,
            AlbumImageUrl = track.AlbumImageUrl,
            ProgressMs = 0,
            DurationMs = track.DurationMs,
            VolumePercent = cached?.VolumePercent ?? 0,
            SupportsVolume = cached?.SupportsVolume ?? true,
            DeviceId = cached?.DeviceId,
            DeviceName = cached?.DeviceName,
            AddedByName = track.AddedByName,
            IsFromBasePlaylist = track.IsFromBasePlaylist
        };
        await _hubContext.Clients.Group(partyId).NowPlayingChanged(dto);
    }

    [HttpPut("device")]
    public async Task<IActionResult> SelectDevice([FromBody] SelectDeviceRequest request)
    {
        var partyId = GetHostPartyId();
        if (partyId == null) return Forbid();

        var success = await _playerService.TransferPlaybackAsync(request.DeviceId);
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }
}
