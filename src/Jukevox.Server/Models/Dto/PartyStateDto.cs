namespace JukeVox.Server.Models.Dto;

public class PartyStateDto
{
    public required string PartyId { get; set; }
    public required string JoinToken { get; set; }
    public bool IsHost { get; set; }
    public bool SpotifyConnected { get; set; }
    public int? CreditsRemaining { get; set; }
    public string? DisplayName { get; set; }
    public int DefaultCredits { get; set; }
    public List<QueueItemDto> Queue { get; set; } = [];
    public PlaybackStateDto? NowPlaying { get; set; }
    public string? BasePlaylistId { get; set; }
    public string? BasePlaylistName { get; set; }
    public Dictionary<string, int>? UserVotes { get; set; }
}
