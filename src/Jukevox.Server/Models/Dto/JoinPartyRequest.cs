namespace Jukevox.Server.Models.Dto;

public class JoinPartyRequest
{
    public required string InviteCode { get; set; }
    public required string DisplayName { get; set; }
}
