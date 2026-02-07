using System.Text.Json.Serialization;

namespace Jukevox.Server.Models.Spotify;

public class SpotifySearchResponse
{
    [JsonPropertyName("tracks")]
    public SpotifyTrackPage? Tracks { get; set; }
}

public class SpotifyTrackPage
{
    [JsonPropertyName("items")]
    public List<SpotifyTrack> Items { get; set; } = [];
}

public class SpotifyTrack
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; set; }

    [JsonPropertyName("artists")]
    public List<SpotifyArtist> Artists { get; set; } = [];

    [JsonPropertyName("album")]
    public SpotifyAlbum? Album { get; set; }
}

public class SpotifyArtist
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class SpotifyAlbum
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("images")]
    public List<SpotifyImage> Images { get; set; } = [];
}

public class SpotifyImage
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }
}
