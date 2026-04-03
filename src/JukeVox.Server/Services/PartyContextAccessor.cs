namespace JukeVox.Server.Services;

public interface IPartyContextAccessor
{
    string? PartyId { get; set; }
}

public class PartyContextAccessor : IPartyContextAccessor
{
    public string? PartyId { get; set; }
}
