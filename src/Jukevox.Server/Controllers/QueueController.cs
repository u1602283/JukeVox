using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using JukeVox.Server.Hubs;
using JukeVox.Server.Middleware;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Services;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/queue")]
public class QueueController : ControllerBase
{
    private readonly QueueService _queueService;
    private readonly PartyService _partyService;
    private readonly SpotifyPlayerService _playerService;
    private readonly PlaybackMonitorService _monitorService;
    private readonly IHubContext<PartyHub, IPartyClient> _hubContext;

    public QueueController(
        QueueService queueService,
        PartyService partyService,
        SpotifyPlayerService playerService,
        PlaybackMonitorService monitorService,
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
        if (!_partyService.IsParticipant(sessionId))
            return Unauthorized();

        return Ok(_queueService.GetQueue());
    }

    [HttpPost]
    public async Task<IActionResult> AddToQueue([FromBody] AddToQueueRequest request)
    {
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsParticipant(sessionId))
            return Unauthorized();

        var (item, error) = _queueService.AddToQueue(sessionId, request);
        if (item == null)
            return BadRequest(new { error });

        var party = _partyService.GetCurrentParty()!;
        var queue = _queueService.GetQueue();

        // Broadcast queue update to all clients
        await _hubContext.Clients.Group(party.Id).QueueUpdated(queue);

        // If this is the first song in the queue, try to start playback immediately
        if (queue.Count == 1 && party.SpotifyTokens != null)
        {
            var playback = await _playerService.GetPlaybackStateAsync();
            if (playback == null || !playback.IsPlaying)
            {
                // Nothing playing — start immediately
                var next = _queueService.Dequeue();
                if (next != null)
                {
                    await _playerService.PlayTrackAsync(next.TrackUri);
                    _monitorService.NotifyTrackStarted(next.TrackUri);
                    queue = _queueService.GetQueue();
                    await _hubContext.Clients.Group(party.Id).QueueUpdated(queue);
                }
            }
            else
            {
                // Spotify is playing (autoplay) — seed our track so it plays next
                await _playerService.AddToQueueAsync(queue[0].TrackUri);
            }
        }

        // Send credits update to the guest who added the song
        if (!_partyService.IsHost(sessionId))
        {
            var guest = _partyService.GetGuest(sessionId);
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
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsHost(sessionId))
            return Forbid();

        if (!_queueService.RemoveFromQueue(id))
            return NotFound();

        var party = _partyService.GetCurrentParty()!;
        var queue = _queueService.GetQueue();
        await _hubContext.Clients.Group(party.Id).QueueUpdated(queue);

        return Ok(queue);
    }

    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] ReorderQueueRequest request)
    {
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsHost(sessionId))
            return Forbid();

        if (!_queueService.Reorder(request.OrderedIds))
            return BadRequest();

        var party = _partyService.GetCurrentParty()!;
        var queue = _queueService.GetQueue();
        await _hubContext.Clients.Group(party.Id).QueueUpdated(queue);

        return Ok(queue);
    }
}
