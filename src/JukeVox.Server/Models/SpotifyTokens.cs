namespace JukeVox.Server.Models;

public class SpotifyTokens
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public DateTime ExpiresAt { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt.AddMinutes(-1);
}
