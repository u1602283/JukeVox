using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using JukeVox.Server.Extensions;
using JukeVox.Server.Hubs;
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

    [HttpPost("pause")]
    public async Task<IActionResult> Pause()
    {
        if (!HttpContext.IsHostAuthenticated())
            return Forbid();

        var success = await _playerService.PauseAsync();
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpPost("resume")]
    public async Task<IActionResult> Resume()
    {
        if (!HttpContext.IsHostAuthenticated())
            return Forbid();

        var success = await _playerService.ResumeAsync();
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpPost("previous")]
    public async Task<IActionResult> Previous([FromQuery] int progressMs = 0)
    {
        if (!HttpContext.IsHostAuthenticated())
            return Forbid();

        // Standard previous-button behavior: restart if >5s in, otherwise go to previous track
        if (progressMs > 5000)
        {
            var success = await _playerService.SeekAsync(0);
            return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
        }

        var prev = _queueService.SkipToPrevious();
        if (prev != null)
        {
            var party = _partyService.GetCurrentParty()!;
            await _playerService.PlayTrackAsync(prev.TrackUri);
            _monitorService.NotifyTrackStarted(prev.TrackUri);

            await BroadcastNowPlaying(party.Id, prev);

            var queue = _queueService.GetQueue();
            await _hubContext.Clients.Group(party.Id).QueueUpdated(queue);
            return Ok();
        }

        // No previous track — restart current track
        var success2 = await _playerService.SeekAsync(0);
        return success2 ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpPost("skip")]
    public async Task<IActionResult> Skip()
    {
        if (!HttpContext.IsHostAuthenticated())
            return Forbid();

        var party = _partyService.GetCurrentParty()!;
        var next = _queueService.Dequeue();

        if (next != null)
        {
            await _playerService.PlayTrackAsync(next.TrackUri);
            _monitorService.NotifyTrackStarted(next.TrackUri);

            await BroadcastNowPlaying(party.Id, next);

            var upcoming = _queueService.GetQueue();
            if (upcoming.Count > 0)
                await _playerService.AddToQueueAsync(upcoming[0].TrackUri);

            await _hubContext.Clients.Group(party.Id).QueueUpdated(upcoming);
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
        if (!HttpContext.IsHostAuthenticated())
            return Forbid();

        var success = await _playerService.SeekAsync(Math.Max(positionMs, 0));
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpPut("volume")]
    public async Task<IActionResult> SetVolume([FromQuery] int percent)
    {
        if (!HttpContext.IsHostAuthenticated())
            return Forbid();

        var success = await _playerService.SetVolumeAsync(Math.Clamp(percent, 0, 100));
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices()
    {
        if (!HttpContext.IsHostAuthenticated())
            return Forbid();

        var devices = await _playerService.GetDevicesAsync();
        return Ok(devices);
    }

    private async Task BroadcastNowPlaying(string partyId, Models.QueueItem track)
    {
        var state = await _playerService.GetPlaybackStateAsync();
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
            VolumePercent = state?.VolumePercent ?? 0,
            SupportsVolume = state?.SupportsVolume ?? true,
            DeviceId = state?.DeviceId,
            DeviceName = state?.DeviceName
        };
        await _hubContext.Clients.Group(partyId).NowPlayingChanged(dto);
    }

    [HttpPut("device")]
    public async Task<IActionResult> SelectDevice([FromBody] SelectDeviceRequest request)
    {
        if (!HttpContext.IsHostAuthenticated())
            return Forbid();

        var success = await _playerService.TransferPlaybackAsync(request.DeviceId);
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }
}
