using JukeVox.Server.Extensions;
using JukeVox.Server.Middleware;
using JukeVox.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace JukeVox.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const string OAuthStateCookie = "JukeVox.OAuthState";
    private readonly ISpotifyAuthService _authService;
    private readonly string _frontendUrl;
    private readonly IPartyService _partyService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(ISpotifyAuthService authService, IPartyService partyService, IConfiguration configuration, ILogger<AuthController> logger)
    {
        _authService = authService;
        _partyService = partyService;
        _frontendUrl = configuration["FrontendUrl"] ?? "http://localhost:5173";
        _logger = logger;
    }

    [HttpGet("login")]
    public IActionResult Login()
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

        var nonce = Guid.NewGuid().ToString("N");
        var state = $"{partyId}:{nonce}";

        Response.Cookies.Append(OAuthStateCookie,
            state,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10),
                Path = "/api/auth/callback"
            });

        var url = _authService.GetAuthorizeUrl(partyId, state);
        return Redirect(url);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error)
    {
        var storedState = Request.Cookies[OAuthStateCookie];
        Response.Cookies.Delete(OAuthStateCookie, new CookieOptions { Path = "/api/auth/callback" });

        // Spotify can redirect back without a code — e.g. error=access_denied (user
        // declined) or error=server_error (Spotify-side fault). Handle it gracefully
        // instead of failing model validation with a raw 400.
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Spotify authorization returned an error: {Error}", error);
            return RedirectToHostWithError(error);
        }

        if (string.IsNullOrEmpty(code))
        {
            _logger.LogWarning("Spotify callback received without an authorization code");
            return RedirectToHostWithError("missing_code");
        }

        if (string.IsNullOrEmpty(storedState) || storedState != state)
        {
            _logger.LogWarning("Spotify callback received with invalid OAuth state");
            return RedirectToHostWithError("invalid_state");
        }

        // Parse partyId from state: "{partyId}:{nonce}"
        var colonIndex = state.IndexOf(':');
        if (colonIndex < 0)
        {
            _logger.LogWarning("Spotify callback received with malformed OAuth state");
            return RedirectToHostWithError("invalid_state");
        }

        var partyId = state[..colonIndex];

        var tokens = await _authService.ExchangeCodeAsync(code, partyId);
        if (tokens == null)
        {
            _logger.LogWarning("Spotify token exchange failed for party {PartyId}", partyId);
            return RedirectToHostWithError("exchange_failed");
        }

        return Redirect($"{_frontendUrl}/host");
    }

    private RedirectResult RedirectToHostWithError(string reason) =>
        Redirect($"{_frontendUrl}/host?spotify_error={Uri.EscapeDataString(reason)}");

    [HttpGet("status")]
    public IActionResult Status()
    {
        var sessionId = HttpContext.GetSessionId();
        var partyId = _partyService.GetPartyIdForSession(sessionId);
        if (partyId == null)
        {
            return Ok(new { connected = false, isExpired = true });
        }

        var party = _partyService.GetParty(partyId);
        return Ok(new
        {
            connected = party?.SpotifyTokens != null,
            isExpired = party?.SpotifyTokens?.IsExpired ?? true
        });
    }
}
