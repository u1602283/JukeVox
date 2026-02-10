using System.Text.Json.Serialization;

namespace JukeVox.Server.Models;

public class QueueItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public required string TrackUri { get; set; }
    public required string TrackName { get; set; }
    public required string ArtistName { get; set; }
    public required string AlbumName { get; set; }
    public string? AlbumImageUrl { get; set; }
    public int DurationMs { get; set; }
    public string AddedBySessionId { get; set; } = string.Empty;
    public string AddedByName { get; set; } = "Host";
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public bool IsFromBasePlaylist { get; set; }
    public int InsertionOrder { get; set; }
    public Dictionary<string, int> Votes { get; set; } = new();

    [JsonIgnore]
    public int Score => Votes.Values.Sum();
}
