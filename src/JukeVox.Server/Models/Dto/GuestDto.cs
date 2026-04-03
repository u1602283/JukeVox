namespace JukeVox.Server.Models.Dto;

public class GuestDto
{
    public required string SessionId { get; set; }
    public required string DisplayName { get; set; }
    public int CreditsRemaining { get; set; }
    public DateTime JoinedAt { get; set; }
}
