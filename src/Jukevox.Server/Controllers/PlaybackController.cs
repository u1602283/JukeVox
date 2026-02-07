using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Jukevox.Server.Hubs;
using Jukevox.Server.Middleware;
using Jukevox.Server.Models.Dto;
using Jukevox.Server.Services;

namespace Jukevox.Server.Controllers;

[ApiController]
[Route("api/playback")]
public class PlaybackController : ControllerBase
{
    private readonly SpotifyPlayerService _playerService;
    private readonly PartyService _partyService;
    private readonly QueueService _queueService;
    private readonly PlaybackMonitorService _monitorService;
    private readonly IHubContext<PartyHub, IPartyClient> _hubContext;

    public PlaybackController(
        SpotifyPlayerService playerService,
        PartyService partyService,
        QueueService queueService,
        PlaybackMonitorService monitorService,
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
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsHost(sessionId))
            return Forbid();

        var success = await _playerService.PauseAsync();
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpPost("resume")]
    public async Task<IActionResult> Resume()
    {
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsHost(sessionId))
            return Forbid();

        var success = await _playerService.ResumeAsync();
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpPost("previous")]
    public async Task<IActionResult> Previous()
    {
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsHost(sessionId))
            return Forbid();

        var success = await _playerService.SkipPreviousAsync();
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpPost("skip")]
    public async Task<IActionResult> Skip()
    {
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsHost(sessionId))
            return Forbid();

        var party = _partyService.GetCurrentParty()!;
        var next = _queueService.Dequeue();

        if (next != null)
        {
            await _playerService.PlayTrackAsync(next.TrackUri);
            _monitorService.NotifyTrackStarted(next.TrackUri);

            // Seed the next track for hardware skip resilience
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
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsHost(sessionId))
            return Forbid();

        var success = await _playerService.SeekAsync(Math.Max(positionMs, 0));
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpPut("volume")]
    public async Task<IActionResult> SetVolume([FromQuery] int percent)
    {
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsHost(sessionId))
            return Forbid();

        var success = await _playerService.SetVolumeAsync(Math.Clamp(percent, 0, 100));
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }

    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices()
    {
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsHost(sessionId))
            return Forbid();

        var devices = await _playerService.GetDevicesAsync();
        return Ok(devices);
    }

    [HttpPut("device")]
    public async Task<IActionResult> SelectDevice([FromBody] SelectDeviceRequest request)
    {
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsHost(sessionId))
            return Forbid();

        var success = await _playerService.TransferPlaybackAsync(request.DeviceId);
        return success ? Ok() : StatusCode(502, new { error = "Spotify API failed" });
    }
}
