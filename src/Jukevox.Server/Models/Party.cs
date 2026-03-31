namespace JukeVox.Server.Models;

public class Party
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public required string InviteCode { get; set; }
    public string HostSessionId { get; set; } = string.Empty;
    public string HostId { get; set; } = string.Empty;
    public int DefaultCredits { get; set; } = 5;
    public SpotifyTokens? SpotifyTokens { get; set; }
    public List<QueueItem> Queue { get; set; } = [];
    public Dictionary<string, GuestSession> Guests { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? BasePlaylistId { get; set; }
    public string? BasePlaylistName { get; set; }
    public List<BasePlaylistTrack> BasePlaylistTracks { get; set; } = [];
    public QueueItem? CurrentTrack { get; set; }
    public List<QueueItem> PlaybackHistory { get; set; } = [];
    public int NextInsertionOrder { get; set; }
}
