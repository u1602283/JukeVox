using Microsoft.AspNetCore.Mvc;
using JukeVox.Server.Extensions;
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

        var party = _partyService.GetCurrentParty()!;
        return Ok(new PartyStateDto
        {
            PartyId = party.Id,
            InviteCode = party.InviteCode,
            IsHost = false,
            SpotifyConnected = party.SpotifyTokens != null,
            CreditsRemaining = guest.CreditsRemaining,
            DisplayName = guest.DisplayName,
            DefaultCredits = party.DefaultCredits,
            Queue = _queueService.GetQueue(),
            BasePlaylistId = party.BasePlaylistId,
            BasePlaylistName = party.BasePlaylistName,
            UserVotes = _queueService.GetUserVotes(sessionId)
        });
    }

    [HttpGet("state")]
    public IActionResult GetState()
    {
        var sessionId = HttpContext.GetSessionId();
        var party = _partyService.GetCurrentParty();
        if (party == null)
            return Ok(new { hasParty = false });

        var isHost = HttpContext.IsHostAuthenticated();
        var isGuest = _partyService.GetGuest(sessionId) != null;

        if (!isHost && !isGuest)
            return Ok(new { hasParty = false });

        var guest = isHost ? null : _partyService.GetGuest(sessionId);

        return Ok(new PartyStateDto
        {
            PartyId = party.Id,
            InviteCode = party.InviteCode,
            IsHost = isHost,
            SpotifyConnected = party.SpotifyTokens != null,
            CreditsRemaining = guest?.CreditsRemaining,
            DisplayName = guest?.DisplayName,
            DefaultCredits = party.DefaultCredits,
            Queue = _queueService.GetQueue(),
            NowPlaying = _monitorService.GetCachedPlaybackState(),
            BasePlaylistId = party.BasePlaylistId,
            BasePlaylistName = party.BasePlaylistName,
            UserVotes = _queueService.GetUserVotes(sessionId)
        });
    }
}
