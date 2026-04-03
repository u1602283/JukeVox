using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using JukeVox.Server.Configuration;
using JukeVox.Server.Models;
using JukeVox.Server.Models.Spotify;

namespace JukeVox.Server.Services;

public class SpotifyAuthService : ISpotifyAuthService
{
    private readonly SpotifyOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IPartyService _partyService;
    private readonly IPartyContextAccessor _partyContextAccessor;
    private readonly ILogger<SpotifyAuthService> _logger;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RefreshLocks = new();

    private static readonly string[] Scopes =
    [
        "user-read-playback-state",
        "user-modify-playback-state",
        "user-read-currently-playing",
        "streaming",
        "playlist-read-private"
    ];

    public SpotifyAuthService(
        IOptions<SpotifyOptions> options,
        HttpClient httpClient,
        IPartyService partyService,
        IPartyContextAccessor partyContextAccessor,
        ILogger<SpotifyAuthService> logger)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _partyService = partyService;
        _partyContextAccessor = partyContextAccessor;
        _logger = logger;
    }

    public string GetAuthorizeUrl(string partyId, string state)
    {
        var scope = string.Join(" ", Scopes);
        return $"https://accounts.spotify.com/authorize?" +
               $"response_type=code&" +
               $"client_id={Uri.EscapeDataString(_options.ClientId)}&" +
               $"scope={Uri.EscapeDataString(scope)}&" +
               $"redirect_uri={Uri.EscapeDataString(_options.RedirectUri)}&" +
               $"state={Uri.EscapeDataString(state)}";
    }

    public async Task<SpotifyTokens?> ExchangeCodeAsync(string code, string partyId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _options.RedirectUri
            })
        };

        AddClientAuth(request);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Spotify token exchange failed: {StatusCode} {Error}", response.StatusCode, error);
            return null;
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<SpotifyTokenResponse>();
        if (tokenResponse == null) return null;

        var tokens = new SpotifyTokens
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken ?? string.Empty,
            ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
        };

        _partyService.SetSpotifyTokens(partyId, tokens);
        return tokens;
    }

    public async Task<string?> GetValidAccessTokenAsync()
    {
        var partyId = _partyContextAccessor.PartyId;
        if (partyId == null) return null;

        var tokens = _partyService.GetSpotifyTokens(partyId);
        if (tokens == null) return null;

        if (!tokens.IsExpired)
            return tokens.AccessToken;

        // Per-party refresh lock to avoid thundering herd
        var refreshLock = RefreshLocks.GetOrAdd(partyId, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync();
        try
        {
            tokens = _partyService.GetSpotifyTokens(partyId);
            if (tokens == null) return null;
            if (!tokens.IsExpired) return tokens.AccessToken;

            return await RefreshTokenAsync(partyId, tokens);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private async Task<string?> RefreshTokenAsync(string partyId, SpotifyTokens tokens)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = tokens.RefreshToken
            })
        };

        AddClientAuth(request);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Spotify token refresh failed: {StatusCode}", response.StatusCode);
            return null;
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<SpotifyTokenResponse>();
        if (tokenResponse == null) return null;

        tokens.AccessToken = tokenResponse.AccessToken;
        tokens.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        if (tokenResponse.RefreshToken != null)
            tokens.RefreshToken = tokenResponse.RefreshToken;

        _partyService.SetSpotifyTokens(partyId, tokens);
        return tokens.AccessToken;
    }

    private void AddClientAuth(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}
