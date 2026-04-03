using JukeVox.Server.Extensions;
using JukeVox.Server.Hubs;
using JukeVox.Server.Middleware;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/queue")]
public class QueueController : ControllerBase
{
    private readonly IHubContext<PartyHub, IPartyClient> _hubContext;
    private readonly IPlaybackMonitorService _monitorService;
    private readonly IPartyService _partyService;
    private readonly ISpotifyPlayerService _playerService;
    private readonly IQueueService _queueService;

    public QueueController(
        IQueueService queueService,
        IPartyService partyService,
        ISpotifyPlayerService playerService,
        IPlaybackMonitorService monitorService,
        IHubContext<PartyHub, IPartyClient> hubContext)
    {
        _queueService = queueService;
        _partyService = partyService;
        _playerService = playerService;
        _monitorService = monitorService;
        _hubContext = hubContext;
    }

    [HttpGet]
    public IActionResult GetQueue()
    {
        var sessionId = HttpContext.GetSessionId();
        var partyId = _partyService.GetPartyIdForSession(sessionId);
        if (partyId == null)
        {
            return Unauthorized();
        }

        var isHost = _partyService.IsHost(partyId, sessionId);
        if (!isHost && !_partyService.IsParticipant(partyId, sessionId))
        {
            return Unauthorized();
        }

        return Ok(new
        {
            queue = _queueService.GetQueue(partyId),
            userVotes = _queueService.GetUserVotes(partyId, sessionId)
        });
    }

    [HttpPost]
    public async Task<IActionResult> AddToQueue([FromBody] AddToQueueRequest request)
    {
        var sessionId = HttpContext.GetSessionId();
        var partyId = _partyService.GetPartyIdForSession(sessionId);
        if (partyId == null)
        {
            return Unauthorized();
        }

        var isHost = _partyService.IsHost(partyId, sessionId);
        if (!isHost && !_partyService.IsParticipant(partyId, sessionId))
        {
            return Unauthorized();
        }

        var (item, error) = _queueService.AddToQueue(partyId, sessionId, request, isHost);
        if (item == null)
        {
            return BadRequest(new { error });
        }

        var party = _partyService.GetParty(partyId)!;
        var queue = _queueService.GetQueue(partyId);

        await _hubContext.Clients.Group(party.Id).QueueUpdated(queue);

        if (queue.Count == 1 && party.SpotifyTokens != null)
        {
            var cachedPlayback = _monitorService.GetCachedPlaybackState(partyId);
            if (cachedPlayback == null || !cachedPlayback.IsPlaying)
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
        }

        if (!isHost)
        {
            var guest = _partyService.GetGuest(partyId, sessionId);
            if (guest != null)
            {
                return Ok(new { queue, creditsRemaining = guest.CreditsRemaining });
            }
        }

        return Ok(new { queue });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> RemoveFromQueue(string id)
    {
        if (!HttpContext.IsHostAuthenticated())
        {
            return Forbid();
        }

        var sessionId = HttpContext.GetSessionId();
        var partyId = _partyService.GetPartyIdForSession(sessionId);
        if (partyId == null)
        {
            return BadRequest(new { error = "No active party" });
        }

        if (!_queueService.RemoveFromQueue(partyId, id))
        {
            return NotFound();
        }

        var queue = _queueService.GetQueue(partyId);
        await _hubContext.Clients.Group(partyId).QueueUpdated(queue);

        return Ok(queue);
    }

    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] ReorderQueueRequest request)
    {
        if (!HttpContext.IsHostAuthenticated())
        {
            return Forbid();
        }

        var sessionId = HttpContext.GetSessionId();
        var partyId = _partyService.GetPartyIdForSession(sessionId);
        if (partyId == null)
        {
            return BadRequest(new { error = "No active party" });
        }

        if (!_queueService.Reorder(partyId, request.OrderedIds))
        {
            return BadRequest();
        }

        var queue = _queueService.GetQueue(partyId);
        await _hubContext.Clients.Group(partyId).QueueUpdated(queue);

        return Ok(queue);
    }

    [HttpPost("{id}/vote")]
    public async Task<IActionResult> Vote(string id, [FromBody] VoteRequest request)
    {
        var sessionId = HttpContext.GetSessionId();
        var partyId = _partyService.GetPartyIdForSession(sessionId);
        if (partyId == null)
        {
            return Unauthorized();
        }

        var isHost = _partyService.IsHost(partyId, sessionId);
        if (!isHost && !_partyService.IsParticipant(partyId, sessionId))
        {
            return Unauthorized();
        }

        var (success, error) = _queueService.Vote(partyId, sessionId, id, request.Vote, isHost);
        if (!success)
        {
            return BadRequest(new { error });
        }

        var queue = _queueService.GetQueue(partyId);
        await _hubContext.Clients.Group(partyId).QueueUpdated(queue);

        var userVotes = _queueService.GetUserVotes(partyId, sessionId);
        userVotes.TryGetValue(id, out var userVote);

        return Ok(new { queue, userVote });
    }
}
