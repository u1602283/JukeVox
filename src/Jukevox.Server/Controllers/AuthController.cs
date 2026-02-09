using Microsoft.AspNetCore.Mvc;
using JukeVox.Server.Extensions;
using JukeVox.Server.Services;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ISpotifyAuthService _authService;
    private readonly IPartyService _partyService;
    private readonly string _frontendUrl;

    public AuthController(ISpotifyAuthService authService, IPartyService partyService, IConfiguration configuration)
    {
        _authService = authService;
        _partyService = partyService;
        _frontendUrl = configuration["FrontendUrl"] ?? "http://localhost:5173";
    }

    private const string OAuthStateCookie = "JukeVox.OAuthState";

    [HttpGet("login")]
    public IActionResult Login()
    {
        if (!HttpContext.IsHostAuthenticated())
            return Forbid();

        var state = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(OAuthStateCookie, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10),
            Path = "/api/auth/callback"
        });

        var url = _authService.GetAuthorizeUrl(state);
        return Redirect(url);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
    {
        var storedState = Request.Cookies[OAuthStateCookie];
        Response.Cookies.Delete(OAuthStateCookie, new CookieOptions { Path = "/api/auth/callback" });

        if (string.IsNullOrEmpty(storedState) || storedState != state)
            return BadRequest("Invalid OAuth state");

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
