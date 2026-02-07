using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using JukeVox.Server.Extensions;
using JukeVox.Server.Hubs;
using JukeVox.Server.Middleware;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Services;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/host/party")]
public class HostPartyController : ControllerBase
{
    private readonly PartyService _partyService;
    private readonly QueueService _queueService;
    private readonly SpotifyPlayerService _playerService;
    private readonly SpotifyPlaylistService _playlistService;
    private readonly PlaybackMonitorService _monitorService;
    private readonly IHubContext<PartyHub, IPartyClient> _hubContext;

    public HostPartyController(
        PartyService partyService,
        QueueService queueService,
        SpotifyPlayerService playerService,
        SpotifyPlaylistService playlistService,
        PlaybackMonitorService monitorService,
        IHubContext<PartyHub, IPartyClient> hubContext)
    {
        _partyService = partyService;
        _queueService = queueService;
        _playerService = playerService;
        _playlistService = playlistService;
        _monitorService = monitorService;
        _hubContext = hubContext;
    }

    [HttpPost]
    public IActionResult CreateParty([FromBody] CreatePartyRequest request)
    {
        if (!HttpContext.IsHostAuthenticated())
            return Unauthorized(new { error = "Host authentication required" });

        var sessionId = HttpContext.GetSessionId();
        var inviteCode = request.InviteCode ?? GenerateInviteCode();
        var party = _partyService.CreateParty(sessionId, inviteCode, request.DefaultCredits);

        return Ok(new PartyStateDto
        {
            PartyId = party.Id,
            InviteCode = party.InviteCode,
            IsHost = true,
            SpotifyConnected = false,
            DefaultCredits = party.DefaultCredits,
            Queue = []
        });
    }

    [HttpPost("resume")]
    public IActionResult ResumeParty()
    {
        if (!HttpContext.IsHostAuthenticated())
            return Unauthorized(new { error = "Host authentication required" });

        var sessionId = HttpContext.GetSessionId();
        var party = _partyService.ResumeAsHost(sessionId);
        if (party == null)
            return BadRequest(new { error = "No saved party to resume" });

        return Ok(new PartyStateDto
        {
            PartyId = party.Id,
            InviteCode = party.InviteCode,
            IsHost = true,
            SpotifyConnected = party.SpotifyTokens != null,
            DefaultCredits = party.DefaultCredits,
            Queue = _queueService.GetQueue(),
            BasePlaylistId = party.BasePlaylistId,
            BasePlaylistName = party.BasePlaylistName
        });
    }

    [HttpGet("saved")]
    public IActionResult GetSavedParty()
    {
        if (!HttpContext.IsHostAuthenticated())
            return Unauthorized(new { error = "Host authentication required" });

        var summary = _partyService.GetSavedPartySummary();
        if (summary == null)
            return Ok(new SavedPartySummaryDto { Exists = false });

        var (inviteCode, queueCount, guestCount, createdAt) = summary.Value;
        return Ok(new SavedPartySummaryDto
        {
            Exists = true,
            InviteCode = inviteCode,
            QueueCount = queueCount,
            GuestCount = guestCount,
            CreatedAt = createdAt
        });
    }

    [HttpPut("settings")]
    public IActionResult UpdateSettings([FromBody] UpdatePartySettingsRequest request)
    {
        if (!HttpContext.IsHostAuthenticated())
            return Unauthorized(new { error = "Host authentication required" });

        _partyService.UpdateSettings(request.InviteCode, request.DefaultCredits);
        return Ok();
    }

    [HttpGet("playlists")]
    public async Task<IActionResult> GetPlaylists([FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        if (!HttpContext.IsHostAuthenticated())
            return Unauthorized(new { error = "Host authentication required" });

        var playlists = await _playlistService.GetUserPlaylistsAsync(limit, offset);
        return Ok(playlists);
    }

    [HttpPut("base-playlist")]
    public async Task<IActionResult> SetBasePlaylist([FromBody] SetBasePlaylistRequest request)
    {
        if (!HttpContext.IsHostAuthenticated())
            return Unauthorized(new { error = "Host authentication required" });

        var tracks = await _playlistService.GetAllPlaylistTracksAsync(request.PlaylistId);
        if (tracks.Count == 0)
            return BadRequest(new { error = "Playlist has no playable tracks" });

        var playlists = await _playlistService.GetUserPlaylistsAsync(50, 0);
        var playlist = playlists.FirstOrDefault(p => p.Id == request.PlaylistId);
        var playlistName = playlist?.Name ?? "Base Playlist";

        _queueService.SetBasePlaylist(tracks, request.PlaylistId, playlistName);

        var party = _partyService.GetCurrentParty()!;
        var queue = _queueService.GetQueue();
        await _hubContext.Clients.Group(party.Id).QueueUpdated(queue);

        // Auto-play if nothing is currently playing
        var playback = await _playerService.GetPlaybackStateAsync();
        if (playback == null || !playback.IsPlaying)
        {
            var next = _queueService.Dequeue();
            if (next != null)
            {
                await _playerService.PlayTrackAsync(next.TrackUri);
                _monitorService.NotifyTrackStarted(next.TrackUri);
                queue = _queueService.GetQueue();
                await _hubContext.Clients.Group(party.Id).QueueUpdated(queue);
            }
        }

        return Ok(new { queue, basePlaylistId = request.PlaylistId, basePlaylistName = playlistName });
    }

    [HttpDelete("base-playlist")]
    public async Task<IActionResult> ClearBasePlaylist()
    {
        if (!HttpContext.IsHostAuthenticated())
            return Unauthorized(new { error = "Host authentication required" });

        _queueService.ClearBasePlaylist();

        var party = _partyService.GetCurrentParty()!;
        var queue = _queueService.GetQueue();
        await _hubContext.Clients.Group(party.Id).QueueUpdated(queue);

        return Ok(new { queue });
    }

    private static string GenerateInviteCode()
    {
        return Random.Shared.Next(1000, 9999).ToString();
    }
}
