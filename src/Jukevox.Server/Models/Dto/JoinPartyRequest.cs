namespace JukeVox.Server.Models.Dto;

public class JoinPartyRequest
{
    public required string JoinToken { get; set; }
    public required string DisplayName { get; set; }
}
