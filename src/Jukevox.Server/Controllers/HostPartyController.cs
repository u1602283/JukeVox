using System.Security.Cryptography;
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
    private readonly IPartyService _partyService;
    private readonly IQueueService _queueService;
    private readonly ISpotifyPlayerService _playerService;
    private readonly ISpotifyPlaylistService _playlistService;
    private readonly IPlaybackMonitorService _monitorService;
    private readonly IHubContext<PartyHub, IPartyClient> _hubContext;
    private readonly ConnectionMapping _connectionMapping;

    public HostPartyController(
        IPartyService partyService,
        IQueueService queueService,
        ISpotifyPlayerService playerService,
        ISpotifyPlaylistService playlistService,
        IPlaybackMonitorService monitorService,
        IHubContext<PartyHub, IPartyClient> hubContext,
        ConnectionMapping connectionMapping)
    {
        _partyService = partyService;
        _queueService = queueService;
        _playerService = playerService;
        _playlistService = playlistService;
        _monitorService = monitorService;
        _hubContext = hubContext;
        _connectionMapping = connectionMapping;
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
            Queue = [],
            UserVotes = new()
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
            BasePlaylistName = party.BasePlaylistName,
            UserVotes = _queueService.GetUserVotes(sessionId)
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

    [HttpGet("guests")]
    public IActionResult GetGuests()
    {
        if (!HttpContext.IsHostAuthenticated())
            return Unauthorized(new { error = "Host authentication required" });

        var guests = _partyService.GetAllGuests();
        var dtos = guests.Select(g => new GuestDto
        {
            SessionId = g.SessionId,
            DisplayName = g.DisplayName,
            CreditsRemaining = g.CreditsRemaining,
            JoinedAt = g.JoinedAt
        }).ToList();

        return Ok(dtos);
    }

    [HttpPut("guests/{sessionId}/credits")]
    public async Task<IActionResult> SetGuestCredits(string sessionId, [FromBody] AdjustCreditsRequest request)
    {
        if (!HttpContext.IsHostAuthenticated())
            return Unauthorized(new { error = "Host authentication required" });

        var guest = _partyService.SetGuestCredits(sessionId, request.Credits);
        if (guest == null)
            return NotFound(new { error = "Guest not found" });

        // Broadcast updated credits to the specific guest via their SignalR connection
        var connectionId = _connectionMapping.GetConnectionId(sessionId);
        if (connectionId != null)
        {
            await _hubContext.Clients.Client(connectionId).CreditsUpdated(guest.CreditsRemaining);
        }

        return Ok(new GuestDto
        {
            SessionId = guest.SessionId,
            DisplayName = guest.DisplayName,
            CreditsRemaining = guest.CreditsRemaining,
            JoinedAt = guest.JoinedAt
        });
    }

    [HttpPost("guests/credits")]
    public async Task<IActionResult> AdjustAllCredits([FromBody] BulkAdjustCreditsRequest request)
    {
        if (!HttpContext.IsHostAuthenticated())
            return Unauthorized(new { error = "Host authentication required" });

        var guests = _partyService.AdjustAllCredits(request.Credits);

        // Broadcast updated credits to each guest via their SignalR connection
        foreach (var guest in guests)
        {
            var connectionId = _connectionMapping.GetConnectionId(guest.SessionId);
            if (connectionId != null)
            {
                await _hubContext.Clients.Client(connectionId).CreditsUpdated(guest.CreditsRemaining);
            }
        }

        var dtos = guests.Select(g => new GuestDto
        {
            SessionId = g.SessionId,
            DisplayName = g.DisplayName,
            CreditsRemaining = g.CreditsRemaining,
            JoinedAt = g.JoinedAt
        }).ToList();

        return Ok(dtos);
    }

    [HttpPost("end")]
    public async Task<IActionResult> EndParty()
    {
        if (!HttpContext.IsHostAuthenticated())
            return Unauthorized(new { error = "Host authentication required" });

        var party = _partyService.GetCurrentParty();
        if (party == null)
            return BadRequest(new { error = "No active party" });

        // Pause Spotify playback
        try
        {
            await _playerService.PauseAsync();
        }
        catch
        {
            // Best effort — Spotify may already be paused or disconnected
        }

        // Broadcast PartyEnded to all clients in the group
        await _hubContext.Clients.Group(party.Id).PartyEnded();

        // Clear party state
        _partyService.EndParty();

        return Ok(new { ended = true });
    }

    private static string GenerateInviteCode()
    {
        const string chars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        return string.Create(6, chars, static (span, chars) =>
        {
            Span<byte> bytes = stackalloc byte[6];
            RandomNumberGenerator.Fill(bytes);
            for (var i = 0; i < span.Length; i++)
                span[i] = chars[bytes[i] % chars.Length];
        });
    }
}
