using Microsoft.AspNetCore.Mvc;
using JukeVox.Server.Middleware;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Services;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/party")]
public class PartyController : ControllerBase
{
    private readonly IPartyService _partyService;
    private readonly IQueueService _queueService;
    private readonly IPlaybackMonitorService _monitorService;

    public PartyController(
        IPartyService partyService,
        IQueueService queueService,
        IPlaybackMonitorService monitorService)
    {
        _partyService = partyService;
        _queueService = queueService;
        _monitorService = monitorService;
    }

    [HttpPost("join")]
    public IActionResult JoinParty([FromBody] JoinPartyRequest request)
    {
        var sessionId = HttpContext.GetSessionId();
        var guest = _partyService.JoinParty(sessionId, request.InviteCode, request.DisplayName);
        if (guest == null)
            return BadRequest(new { error = "Invalid invite code or no active party" });

        var partyId = _partyService.GetPartyIdForSession(sessionId)!;
        var party = _partyService.GetParty(partyId)!;
        return Ok(new PartyStateDto
        {
            PartyId = party.Id,
            InviteCode = party.InviteCode,
            IsHost = false,
            SpotifyConnected = party.SpotifyTokens != null,
            CreditsRemaining = guest.CreditsRemaining,
            DisplayName = guest.DisplayName,
            DefaultCredits = party.DefaultCredits,
            Queue = _queueService.GetQueue(partyId),
            BasePlaylistId = party.BasePlaylistId,
            BasePlaylistName = party.BasePlaylistName,
            UserVotes = _queueService.GetUserVotes(partyId, sessionId)
        });
    }

    [HttpGet("state")]
    public IActionResult GetState()
    {
        var sessionId = HttpContext.GetSessionId();
        var partyId = _partyService.GetPartyIdForSession(sessionId);
        if (partyId == null)
            return Ok(new { hasParty = false });

        var party = _partyService.GetParty(partyId);
        if (party == null)
            return Ok(new { hasParty = false });

        var isHost = _partyService.IsHost(partyId, sessionId);
        var isGuest = _partyService.GetGuest(partyId, sessionId) != null;

        if (!isHost && !isGuest)
            return Ok(new { hasParty = false });

        var guest = isHost ? null : _partyService.GetGuest(partyId, sessionId);

        return Ok(new PartyStateDto
        {
            PartyId = party.Id,
            InviteCode = party.InviteCode,
            IsHost = isHost,
            SpotifyConnected = party.SpotifyTokens != null,
            CreditsRemaining = guest?.CreditsRemaining,
            DisplayName = guest?.DisplayName,
            DefaultCredits = party.DefaultCredits,
            Queue = _queueService.GetQueue(partyId),
            NowPlaying = _monitorService.GetCachedPlaybackState(partyId),
            BasePlaylistId = party.BasePlaylistId,
            BasePlaylistName = party.BasePlaylistName,
            UserVotes = _queueService.GetUserVotes(partyId, sessionId)
        });
    }
}
