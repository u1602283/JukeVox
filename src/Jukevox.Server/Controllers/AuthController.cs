using Microsoft.AspNetCore.Mvc;
using JukeVox.Server.Middleware;
using JukeVox.Server.Services;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly SpotifyAuthService _authService;
    private readonly PartyService _partyService;

    public AuthController(SpotifyAuthService authService, PartyService partyService)
    {
        _authService = authService;
        _partyService = partyService;
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        var sessionId = HttpContext.GetSessionId();
        if (!_partyService.IsHost(sessionId))
            return Forbid();

        var state = sessionId;
        var url = _authService.GetAuthorizeUrl(state);
        return Redirect(url);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
    {
        var tokens = await _authService.ExchangeCodeAsync(code);
        if (tokens == null)
            return BadRequest("Failed to exchange authorization code");

        // Redirect back to the Vite frontend
        return Redirect("http://localhost:5173/");
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        var party = _partyService.GetCurrentParty();
        return Ok(new
        {
            connected = party?.SpotifyTokens != null,
            isExpired = party?.SpotifyTokens?.IsExpired ?? true
        });
    }
}
