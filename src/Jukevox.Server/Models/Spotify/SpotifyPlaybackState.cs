using System.Text.Json.Serialization;

namespace JukeVox.Server.Models.Spotify;

public class SpotifyPlaybackState
{
    [JsonPropertyName("is_playing")]
    public bool IsPlaying { get; set; }

    [JsonPropertyName("progress_ms")]
    public int? ProgressMs { get; set; }

    [JsonPropertyName("item")]
    public SpotifyTrack? Item { get; set; }

    [JsonPropertyName("device")]
    public SpotifyDevice? Device { get; set; }
}

public class SpotifyDevice
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    [JsonPropertyName("volume_percent")]
    public int? VolumePercent { get; set; }
}

public class SpotifyDevicesResponse
{
    [JsonPropertyName("devices")]
    public List<SpotifyDevice> Devices { get; set; } = [];
}

public class SpotifyQueueResponse
{
    [JsonPropertyName("currently_playing")]
    public SpotifyTrack? CurrentlyPlaying { get; set; }

    [JsonPropertyName("queue")]
    public List<SpotifyTrack> Queue { get; set; } = [];
}
