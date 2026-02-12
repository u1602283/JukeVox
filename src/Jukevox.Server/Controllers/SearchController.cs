using Microsoft.AspNetCore.Mvc;
using JukeVox.Server.Extensions;
using JukeVox.Server.Middleware;
using JukeVox.Server.Services;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly ISpotifySearchService _searchService;
    private readonly IPartyService _partyService;

    public SearchController(ISpotifySearchService searchService, IPartyService partyService)
    {
        _searchService = searchService;
        _partyService = partyService;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 20)
    {
        var sessionId = HttpContext.GetSessionId();
        var isHost = HttpContext.IsHostAuthenticated();
        if (!isHost && !_partyService.IsParticipant(sessionId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(q))
            return Ok(Array.Empty<object>());

        var results = await _searchService.SearchAsync(q, Math.Clamp(limit, 1, 50));
        return Ok(results);
    }
}
