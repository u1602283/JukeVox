namespace Jukevox.Server.Models;

public class GuestSession
{
    public required string SessionId { get; set; }
    public required string DisplayName { get; set; }
    public int CreditsRemaining { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
