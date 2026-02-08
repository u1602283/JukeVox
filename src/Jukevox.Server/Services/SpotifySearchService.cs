using System.Net.Http.Headers;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Models.Spotify;

namespace JukeVox.Server.Services;

public class SpotifySearchService : ISpotifySearchService
{
    private readonly HttpClient _httpClient;
    private readonly ISpotifyAuthService _authService;
    private readonly ILogger<SpotifySearchService> _logger;

    public SpotifySearchService(
        HttpClient httpClient,
        ISpotifyAuthService authService,
        ILogger<SpotifySearchService> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _logger = logger;
    }

    public async Task<List<SearchResultDto>> SearchAsync(string query, int limit = 20)
    {
        var token = await _authService.GetValidAccessTokenAsync();
        if (token == null) return [];

        var url = $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(query)}&type=track&limit={limit}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Spotify search failed: {StatusCode}", response.StatusCode);
            return [];
        }

        var result = await response.Content.ReadFromJsonAsync<SpotifySearchResponse>();
        if (result?.Tracks?.Items == null) return [];

        return result.Tracks.Items.Select(t => new SearchResultDto
        {
            TrackUri = t.Uri,
            TrackName = t.Name,
            ArtistName = string.Join(", ", t.Artists.Select(a => a.Name)),
            AlbumName = t.Album?.Name ?? string.Empty,
            AlbumImageUrl = t.Album?.Images.FirstOrDefault()?.Url,
            DurationMs = t.DurationMs
        }).ToList();
    }
}
