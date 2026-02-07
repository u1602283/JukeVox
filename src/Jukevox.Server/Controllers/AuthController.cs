using Microsoft.AspNetCore.Mvc;
using JukeVox.Server.Extensions;
using JukeVox.Server.Services;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly SpotifyAuthService _authService;
    private readonly PartyService _partyService;
    private readonly string _frontendUrl;

    public AuthController(SpotifyAuthService authService, PartyService partyService, IConfiguration configuration)
    {
        _authService = authService;
        _partyService = partyService;
        _frontendUrl = configuration["FrontendUrl"] ?? "http://localhost:5173";
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        if (!HttpContext.IsHostAuthenticated())
            return Forbid();

        var state = Guid.NewGuid().ToString("N");
        var url = _authService.GetAuthorizeUrl(state);
        return Redirect(url);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
    {
        var tokens = await _authService.ExchangeCodeAsync(code);
        if (tokens == null)
            return BadRequest("Failed to exchange authorization code");

        return Redirect($"{_frontendUrl}/host");
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
