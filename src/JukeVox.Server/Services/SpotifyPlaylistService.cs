using System.Net;
using System.Net.Http.Headers;
using JukeVox.Server.Models;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Models.Spotify;

namespace JukeVox.Server.Services;

public class SpotifyPlaylistService : ISpotifyPlaylistService
{
    private const int MaxRetries = 5;
    private readonly ISpotifyAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SpotifyPlaylistService> _logger;

    public SpotifyPlaylistService(
        HttpClient httpClient,
        ISpotifyAuthService authService,
        ILogger<SpotifyPlaylistService> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _logger = logger;
    }

    public async Task<List<SpotifyPlaylistDto>> GetUserPlaylistsAsync(int limit = 50, int offset = 0)
    {
        var url = $"https://api.spotify.com/v1/me/playlists?limit={limit}&offset={offset}";
        var response = await SendAsync(HttpMethod.Get, url);
        if (response == null || !response.IsSuccessStatusCode)
        {
            return [];
        }

        var result = await response.Content.ReadFromJsonAsync<SpotifyPlaylistsResponse>();
        if (result == null)
        {
            return [];
        }

        return result.Items.Select(p => new SpotifyPlaylistDto
            {
                Id = p.Id,
                Name = p.Name,
                ImageUrl = p.Images.FirstOrDefault()?.Url,
                TrackCount = p.Tracks?.Total ?? 0
            })
            .ToList();
    }

    public async Task<List<BasePlaylistTrack>> GetAllPlaylistTracksAsync(string playlistId)
    {
        var tracks = new List<BasePlaylistTrack>();
        var fields = "items(track(uri,name,duration_ms,artists(name),album(name,images)),is_local),next,total";
        var url =
            $"https://api.spotify.com/v1/playlists/{playlistId}/tracks?fields={Uri.EscapeDataString(fields)}&limit=100";

        while (url != null)
        {
            var response = await SendAsync(HttpMethod.Get, url);
            if (response == null || !response.IsSuccessStatusCode)
            {
                break;
            }

            var result = await response.Content.ReadFromJsonAsync<SpotifyPlaylistTracksResponse>();
            if (result == null)
            {
                break;
            }

            foreach (var item in result.Items)
            {
                if (item.IsLocal || item.Track == null)
                {
                    continue;
                }

                tracks.Add(new BasePlaylistTrack
                {
                    TrackUri = item.Track.Uri,
                    TrackName = item.Track.Name,
                    ArtistName = string.Join(", ", item.Track.Artists.Select(a => a.Name)),
                    AlbumName = item.Track.Album?.Name ?? string.Empty,
                    AlbumImageUrl = item.Track.Album?.Images.FirstOrDefault()?.Url,
                    DurationMs = item.Track.DurationMs
                });
            }

            url = result.Next;
        }

        return tracks;
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpMethod method, string url)
    {
        for (var attempt = 0;; attempt++)
        {
            var token = await _authService.GetValidAccessTokenAsync();
            if (token == null)
            {
                return null;
            }

            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                    _logger.LogWarning("Spotify rate limited (attempt {Attempt}/{Max}). Retry after: {RetryAfter}",
                        attempt + 1,
                        MaxRetries,
                        retryAfter);
                    response.Dispose();
                    await Task.Delay(retryAfter);
                    continue;
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Spotify API call failed: {Method} {Url}", method, url);
                return null;
            }
        }
    }
}
