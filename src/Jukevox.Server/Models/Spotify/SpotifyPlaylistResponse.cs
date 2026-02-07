using System.Text.Json.Serialization;

namespace JukeVox.Server.Models.Spotify;

public class SpotifyPlaylistsResponse
{
    [JsonPropertyName("items")]
    public List<SpotifyPlaylistSummary> Items { get; set; } = [];

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class SpotifyPlaylistSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("images")]
    public List<SpotifyImage> Images { get; set; } = [];

    [JsonPropertyName("tracks")]
    public SpotifyPlaylistTracksRef? Tracks { get; set; }
}

public class SpotifyPlaylistTracksRef
{
    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class SpotifyPlaylistTracksResponse
{
    [JsonPropertyName("items")]
    public List<SpotifyPlaylistTrackItem> Items { get; set; } = [];

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class SpotifyPlaylistTrackItem
{
    [JsonPropertyName("track")]
    public SpotifyTrack? Track { get; set; }

    [JsonPropertyName("is_local")]
    public bool IsLocal { get; set; }
}
