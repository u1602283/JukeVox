using JukeVox.Server.Middleware;
using JukeVox.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly IPartyService _partyService;
    private readonly ISpotifySearchService _searchService;

    public SearchController(ISpotifySearchService searchService, IPartyService partyService)
    {
        _searchService = searchService;
        _partyService = partyService;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 20)
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

        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(Array.Empty<object>());
        }

        var results = await _searchService.SearchAsync(q, Math.Clamp(limit, 1, 50));
        return Ok(results);
    }
}
