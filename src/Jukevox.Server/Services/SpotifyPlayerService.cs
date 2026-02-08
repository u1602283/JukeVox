using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Models.Spotify;

namespace JukeVox.Server.Services;

public class SpotifyPlayerService : ISpotifyPlayerService
{
    private readonly HttpClient _httpClient;
    private readonly ISpotifyAuthService _authService;
    private readonly ILogger<SpotifyPlayerService> _logger;

    private const string BaseUrl = "https://api.spotify.com/v1/me/player";

    public SpotifyPlayerService(
        HttpClient httpClient,
        ISpotifyAuthService authService,
        ILogger<SpotifyPlayerService> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _logger = logger;
    }

    public async Task<PlaybackStateDto?> GetPlaybackStateAsync()
    {
        var response = await SendAsync(HttpMethod.Get, BaseUrl);
        if (response == null || response.StatusCode == HttpStatusCode.NoContent)
            return null;

        if (!response.IsSuccessStatusCode) return null;

        var state = await response.Content.ReadFromJsonAsync<SpotifyPlaybackState>();
        if (state == null) return null;

        return new PlaybackStateDto
        {
            IsPlaying = state.IsPlaying,
            TrackUri = state.Item?.Uri,
            TrackName = state.Item?.Name,
            ArtistName = state.Item != null
                ? string.Join(", ", state.Item.Artists.Select(a => a.Name))
                : null,
            AlbumName = state.Item?.Album?.Name,
            AlbumImageUrl = state.Item?.Album?.Images.FirstOrDefault()?.Url,
            ProgressMs = state.ProgressMs ?? 0,
            DurationMs = state.Item?.DurationMs ?? 0,
            VolumePercent = state.Device?.VolumePercent ?? 0,
            SupportsVolume = state.Device?.SupportsVolume ?? true,
            DeviceId = state.Device?.Id,
            DeviceName = state.Device?.Name
        };
    }

    public async Task<bool> PlayTrackAsync(string trackUri, string? deviceId = null)
    {
        var url = BaseUrl + "/play";
        if (deviceId != null) url += $"?device_id={deviceId}";

        var body = JsonSerializer.Serialize(new { uris = new[] { trackUri } });
        var response = await SendAsync(HttpMethod.Put, url, body);
        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<bool> ResumeAsync()
    {
        var response = await SendAsync(HttpMethod.Put, BaseUrl + "/play");
        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<bool> PauseAsync()
    {
        var response = await SendAsync(HttpMethod.Put, BaseUrl + "/pause");
        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<bool> SkipNextAsync()
    {
        var response = await SendAsync(HttpMethod.Post, BaseUrl + "/next");
        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<bool> SkipPreviousAsync()
    {
        var response = await SendAsync(HttpMethod.Post, BaseUrl + "/previous");
        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<bool> SeekAsync(int positionMs)
    {
        var response = await SendAsync(HttpMethod.Put, $"{BaseUrl}/seek?position_ms={positionMs}");
        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<bool> SetVolumeAsync(int percent)
    {
        var response = await SendAsync(HttpMethod.Put, $"{BaseUrl}/volume?volume_percent={percent}");
        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<List<SpotifyDeviceDto>> GetDevicesAsync()
    {
        var response = await SendAsync(HttpMethod.Get, BaseUrl + "/devices");
        if (response == null || !response.IsSuccessStatusCode) return [];

        var result = await response.Content.ReadFromJsonAsync<SpotifyDevicesResponse>();
        if (result == null) return [];

        return result.Devices.Select(d => new SpotifyDeviceDto
        {
            Id = d.Id,
            Name = d.Name,
            Type = d.Type,
            IsActive = d.IsActive,
            VolumePercent = d.VolumePercent ?? 0,
            SupportsVolume = d.SupportsVolume
        }).ToList();
    }

    public async Task<bool> AddToQueueAsync(string trackUri)
    {
        var response = await SendAsync(HttpMethod.Post, $"{BaseUrl}/queue?uri={Uri.EscapeDataString(trackUri)}");
        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<List<string>> GetSpotifyQueueAsync()
    {
        var response = await SendAsync(HttpMethod.Get, BaseUrl + "/queue");
        if (response == null || !response.IsSuccessStatusCode) return [];

        var result = await response.Content.ReadFromJsonAsync<SpotifyQueueResponse>();
        return result?.Queue.Select(t => t.Uri).ToList() ?? [];
    }

    public async Task<bool> TransferPlaybackAsync(string deviceId)
    {
        var body = JsonSerializer.Serialize(new { device_ids = new[] { deviceId } });
        var response = await SendAsync(HttpMethod.Put, BaseUrl, body);
        return response?.IsSuccessStatusCode ?? false;
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpMethod method, string url, string? jsonBody = null)
    {
        var token = await _authService.GetValidAccessTokenAsync();
        if (token == null) return null;

        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (jsonBody != null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                _logger.LogWarning("Spotify rate limited. Retry after: {RetryAfter}", retryAfter);
                await Task.Delay(retryAfter);
                return await SendAsync(method, url, jsonBody);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Spotify API error: {Method} {Url} → {Status} {Body}",
                    method, url, (int)response.StatusCode, body);
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
