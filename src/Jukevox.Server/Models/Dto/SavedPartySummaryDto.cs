namespace JukeVox.Server.Models.Dto;

public class SavedPartySummaryDto
{
    public bool Exists { get; set; }
    public string? InviteCode { get; set; }
    public int QueueCount { get; set; }
    public int GuestCount { get; set; }
    public DateTime? CreatedAt { get; set; }
}
