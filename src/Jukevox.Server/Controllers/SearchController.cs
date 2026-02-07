using Microsoft.AspNetCore.Mvc;
using Jukevox.Server.Middleware;
using Jukevox.Server.Services;

namespace Jukevox.Server.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly SpotifySearchService _searchService;
    private readonly PartyService _partyService;

    public SearchController(SpotifySearchService searchService, PartyService partyService)
    {
        _searchService = searchService;
        _partyService = partyService;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 20)
    {
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsParticipant(sessionId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(q))
            return Ok(Array.Empty<object>());

        var results = await _searchService.SearchAsync(q, Math.Clamp(limit, 1, 50));
        return Ok(results);
    }
}
