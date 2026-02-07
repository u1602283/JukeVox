using Microsoft.AspNetCore.Mvc;
using JukeVox.Server.Middleware;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Services;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/party")]
public class PartyController : ControllerBase
{
    private readonly PartyService _partyService;
    private readonly QueueService _queueService;
    private readonly SpotifyPlayerService _playerService;

    public PartyController(
        PartyService partyService,
        QueueService queueService,
        SpotifyPlayerService playerService)
    {
        _partyService = partyService;
        _queueService = queueService;
        _playerService = playerService;
    }

    [HttpPost]
    public IActionResult CreateParty([FromBody] CreatePartyRequest request)
    {
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
            Queue = _queueService.GetQueue()
        });
    }

    [HttpGet("state")]
    public async Task<IActionResult> GetState()
    {
        var sessionId = HttpContext.GetSessionId();
        var party = _partyService.GetCurrentParty();
        if (party == null)
            return Ok(new { hasParty = false });

        if (!_partyService.IsParticipant(sessionId))
            return Ok(new { hasParty = false });

        var isHost = _partyService.IsHost(sessionId);
        var guest = isHost ? null : _partyService.GetGuest(sessionId);

        PlaybackStateDto? nowPlaying = null;
        if (party.SpotifyTokens != null)
        {
            nowPlaying = await _playerService.GetPlaybackStateAsync();
        }

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
            NowPlaying = nowPlaying
        });
    }

    [HttpPut("settings")]
    public IActionResult UpdateSettings([FromBody] UpdatePartySettingsRequest request)
    {
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsHost(sessionId))
            return Forbid();

        _partyService.UpdateSettings(request.InviteCode, request.DefaultCredits);
        return Ok();
    }

    [HttpGet("saved")]
    public IActionResult GetSavedParty()
    {
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

    [HttpPost("resume")]
    public IActionResult ResumeParty()
    {
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
            Queue = _queueService.GetQueue()
        });
    }

    private static string GenerateInviteCode()
    {
        return Random.Shared.Next(1000, 9999).ToString();
    }
}
