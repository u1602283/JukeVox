namespace Jukevox.Server.Models.Dto;

public class PartyStateDto
{
    public required string PartyId { get; set; }
    public required string InviteCode { get; set; }
    public bool IsHost { get; set; }
    public bool SpotifyConnected { get; set; }
    public int? CreditsRemaining { get; set; }
    public string? DisplayName { get; set; }
    public int DefaultCredits { get; set; }
    public List<QueueItemDto> Queue { get; set; } = [];
    public PlaybackStateDto? NowPlaying { get; set; }
}
