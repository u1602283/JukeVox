namespace JukeVox.Server.Models.Dto;

public class CreatePartyRequest
{
    public string? InviteCode { get; set; }
    public int DefaultCredits { get; set; } = 5;
}
