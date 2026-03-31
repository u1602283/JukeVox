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
    private readonly HostCredentialService _credentialService;

    public HostPartyController(
        IPartyService partyService,
        IQueueService queueService,
        ISpotifyPlayerService playerService,
        ISpotifyPlaylistService playlistService,
        IPlaybackMonitorService monitorService,
        IHubContext<PartyHub, IPartyClient> hubContext,
        ConnectionMapping connectionMapping,
        HostCredentialService credentialService)
    {
        _partyService = partyService;
        _queueService = queueService;
        _playerService = playerService;
        _playlistService = playlistService;
        _monitorService = monitorService;
        _hubContext = hubContext;
        _connectionMapping = connectionMapping;
        _credentialService = credentialService;
    }

    private string? RequireHostAuth()
    {
        var hostId = HttpContext.GetAuthenticatedHostId();
        if (hostId == null) return null;
        return hostId;
    }

    private string? GetActivePartyId()
    {
        var sessionId = HttpContext.GetSessionId();
        return _partyService.GetPartyIdForSession(sessionId);
    }

    [HttpPost]
    public IActionResult CreateParty([FromBody] CreatePartyRequest request)
    {
        var hostId = RequireHostAuth();
        if (hostId == null)
            return Unauthorized(new { error = "Host authentication required" });

        if (request.DefaultCredits < 1)
            return BadRequest(new { error = "Credits per guest must be at least 1" });

        if (_partyService.GetPartiesForHost(hostId).Count > 0)
            return Conflict(new { error = "You already have an active party" });

        var sessionId = HttpContext.GetSessionId();
        var inviteCode = request.InviteCode ?? GenerateInviteCode();
        var party = _partyService.CreateParty(sessionId, hostId, inviteCode, request.DefaultCredits);

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

    [HttpGet("~/api/host/parties")]
    public IActionResult GetParties()
    {
        var hostId = RequireHostAuth();
        if (hostId == null)
            return Unauthorized(new { error = "Host authentication required" });

        var isAdmin = _credentialService.IsAdmin(hostId);
        var summaries = _partyService.GetAllPartySummaries();

        // Non-admin hosts only see their own parties
        if (!isAdmin)
            summaries = summaries.Where(s => s.HostId == hostId).ToList();

        var result = summaries.Select(s => new
        {
            partyId = s.PartyId,
            inviteCode = s.InviteCode,
            hostId = s.HostId,
            queueCount = s.QueueCount,
            guestCount = s.GuestCount,
            createdAt = s.CreatedAt
        });

        return Ok(result);
    }

    [HttpPost("~/api/host/parties/{partyId}/select")]
    public IActionResult SelectParty(string partyId)
    {
        var hostId = RequireHostAuth();
        if (hostId == null)
            return Unauthorized(new { error = "Host authentication required" });

        var party = _partyService.GetParty(partyId);
        if (party == null)
            return NotFound(new { error = "Party not found" });

        // Only the owning host (or admin) can select a party
        var isAdmin = _credentialService.IsAdmin(hostId);
        if (party.HostId != hostId && !isAdmin)
            return Forbid();

        var sessionId = HttpContext.GetSessionId();
        _partyService.ResumeAsHost(partyId, sessionId);

        return Ok(new PartyStateDto
        {
            PartyId = party.Id,
            InviteCode = party.InviteCode,
            IsHost = true,
            SpotifyConnected = party.SpotifyTokens != null,
            DefaultCredits = party.DefaultCredits,
            Queue = _queueService.GetQueue(partyId),
            BasePlaylistId = party.BasePlaylistId,
            BasePlaylistName = party.BasePlaylistName,
            UserVotes = _queueService.GetUserVotes(partyId, sessionId)
        });
    }

    [HttpPost("resume")]
    public IActionResult ResumeParty()
    {
        var hostId = RequireHostAuth();
        if (hostId == null)
            return Unauthorized(new { error = "Host authentication required" });

        // Resume the first party owned by this host
        var parties = _partyService.GetPartiesForHost(hostId);
        if (parties.Count == 0)
            return BadRequest(new { error = "No saved party to resume" });

        var sessionId = HttpContext.GetSessionId();
        var party = _partyService.ResumeAsHost(parties[0].Id, sessionId);
        if (party == null)
            return BadRequest(new { error = "No saved party to resume" });

        return Ok(new PartyStateDto
        {
            PartyId = party.Id,
            InviteCode = party.InviteCode,
            IsHost = true,
            SpotifyConnected = party.SpotifyTokens != null,
            DefaultCredits = party.DefaultCredits,
            Queue = _queueService.GetQueue(party.Id),
            BasePlaylistId = party.BasePlaylistId,
            BasePlaylistName = party.BasePlaylistName,
            UserVotes = _queueService.GetUserVotes(party.Id, sessionId)
        });
    }

    [HttpGet("saved")]
    public IActionResult GetSavedParty()
    {
        var hostId = RequireHostAuth();
        if (hostId == null)
            return Unauthorized(new { error = "Host authentication required" });

        var parties = _partyService.GetPartiesForHost(hostId);
        if (parties.Count == 0)
            return Ok(new SavedPartySummaryDto { Exists = false });

        var p = parties[0];
        return Ok(new SavedPartySummaryDto
        {
            Exists = true,
            InviteCode = p.InviteCode,
            QueueCount = p.Queue.Count,
            GuestCount = p.Guests.Count,
            CreatedAt = p.CreatedAt
        });
    }

    [HttpPut("settings")]
    public IActionResult UpdateSettings([FromBody] UpdatePartySettingsRequest request)
    {
        var hostId = RequireHostAuth();
        if (hostId == null)
            return Unauthorized(new { error = "Host authentication required" });

        var partyId = GetActivePartyId();
        if (partyId == null) return BadRequest(new { error = "No active party" });

        if (request.DefaultCredits.HasValue && request.DefaultCredits.Value < 1)
            return BadRequest(new { error = "Credits per guest must be at least 1" });

        _partyService.UpdateSettings(partyId, request.InviteCode, request.DefaultCredits);
        return Ok();
    }

    [HttpGet("playlists")]
    public async Task<IActionResult> GetPlaylists([FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        if (RequireHostAuth() == null)
            return Unauthorized(new { error = "Host authentication required" });

        var playlists = await _playlistService.GetUserPlaylistsAsync(limit, offset);
        return Ok(playlists);
    }

    [HttpPut("base-playlist")]
    public async Task<IActionResult> SetBasePlaylist([FromBody] SetBasePlaylistRequest request)
    {
        if (RequireHostAuth() == null)
            return Unauthorized(new { error = "Host authentication required" });

        var partyId = GetActivePartyId();
        if (partyId == null) return BadRequest(new { error = "No active party" });

        var tracks = await _playlistService.GetAllPlaylistTracksAsync(request.PlaylistId);
        if (tracks.Count == 0)
            return BadRequest(new { error = "Playlist has no playable tracks" });

        var playlists = await _playlistService.GetUserPlaylistsAsync(50, 0);
        var playlist = playlists.FirstOrDefault(p => p.Id == request.PlaylistId);
        var playlistName = playlist?.Name ?? "Base Playlist";

        _queueService.SetBasePlaylist(partyId, tracks, request.PlaylistId, playlistName);

        var party = _partyService.GetParty(partyId)!;
        var queue = _queueService.GetQueue(partyId);
        await _hubContext.Clients.Group(party.Id).QueueUpdated(queue);

        var cachedPlayback = _monitorService.GetCachedPlaybackState(partyId);
        var isPlayingOurTrack = cachedPlayback?.IsPlaying == true
            && party.CurrentTrack != null
            && cachedPlayback.TrackUri == party.CurrentTrack.TrackUri;

        if (!isPlayingOurTrack)
        {
            var next = _queueService.Dequeue(partyId);
            if (next != null)
            {
                await _playerService.PlayTrackAsync(next.TrackUri);
                _monitorService.NotifyTrackStarted(partyId, next.TrackUri);
                queue = _queueService.GetQueue(partyId);
                await _hubContext.Clients.Group(party.Id).QueueUpdated(queue);
            }
        }

        return Ok(new { queue, basePlaylistId = request.PlaylistId, basePlaylistName = playlistName });
    }

    [HttpDelete("base-playlist")]
    public async Task<IActionResult> ClearBasePlaylist()
    {
        if (RequireHostAuth() == null)
            return Unauthorized(new { error = "Host authentication required" });

        var partyId = GetActivePartyId();
        if (partyId == null) return BadRequest(new { error = "No active party" });

        _queueService.ClearBasePlaylist(partyId);

        var queue = _queueService.GetQueue(partyId);
        await _hubContext.Clients.Group(partyId).QueueUpdated(queue);

        return Ok(new { queue });
    }

    [HttpGet("guests")]
    public IActionResult GetGuests()
    {
        if (RequireHostAuth() == null)
            return Unauthorized(new { error = "Host authentication required" });

        var partyId = GetActivePartyId();
        if (partyId == null) return BadRequest(new { error = "No active party" });

        var guests = _partyService.GetAllGuests(partyId);
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
        if (RequireHostAuth() == null)
            return Unauthorized(new { error = "Host authentication required" });

        var partyId = GetActivePartyId();
        if (partyId == null) return BadRequest(new { error = "No active party" });

        var guest = _partyService.SetGuestCredits(partyId, sessionId, request.Credits);
        if (guest == null)
            return NotFound(new { error = "Guest not found" });

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
        if (RequireHostAuth() == null)
            return Unauthorized(new { error = "Host authentication required" });

        var partyId = GetActivePartyId();
        if (partyId == null) return BadRequest(new { error = "No active party" });

        var guests = _partyService.AdjustAllCredits(partyId, request.Credits);

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

    [HttpDelete("guests/{sessionId}")]
    public async Task<IActionResult> KickGuest(string sessionId)
    {
        if (RequireHostAuth() == null)
            return Unauthorized(new { error = "Host authentication required" });

        var partyId = GetActivePartyId();
        if (partyId == null) return BadRequest(new { error = "No active party" });

        if (!_partyService.RemoveGuest(partyId, sessionId))
            return NotFound(new { error = "Guest not found" });

        var connectionId = _connectionMapping.GetConnectionId(sessionId);
        if (connectionId != null)
        {
            await _hubContext.Clients.Client(connectionId).PartyEnded();
        }

        return NoContent();
    }

    [HttpPost("end")]
    public async Task<IActionResult> EndParty()
    {
        if (RequireHostAuth() == null)
            return Unauthorized(new { error = "Host authentication required" });

        var partyId = GetActivePartyId();
        if (partyId == null)
            return BadRequest(new { error = "No active party" });

        try
        {
            await _playerService.PauseAsync();
        }
        catch
        {
            // Best effort
        }

        await _hubContext.Clients.Group(partyId).PartyEnded();

        _partyService.EndParty(partyId);

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
