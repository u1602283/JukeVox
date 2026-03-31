using JukeVox.Server.Models;

namespace JukeVox.Server.Services;

public interface IPartyService
{
    Party? GetParty(string partyId);
    string? GetPartyIdForSession(string sessionId);
    List<Party> GetAllParties();
    List<Party> GetPartiesForHost(string hostId);
    Party CreateParty(string hostSessionId, string hostId, int defaultCredits);
    (GuestSession? Guest, string? Error) JoinParty(string sessionId, string joinToken, string displayName);
    bool IsHost(string partyId, string sessionId);
    bool IsParticipant(string partyId, string sessionId);
    GuestSession? GetGuest(string partyId, string sessionId);
    void UpdateSettings(string partyId, int? defaultCredits);
    void SetSpotifyTokens(string partyId, SpotifyTokens tokens);
    SpotifyTokens? GetSpotifyTokens(string partyId);
    List<(string PartyId, string JoinToken, string HostId, int QueueCount, int GuestCount, DateTime CreatedAt)> GetAllPartySummaries();
    (string? DisplayName, string? Error) TrySpendCredit(string partyId, string sessionId);
    Party? ResumeAsHost(string partyId, string newHostSessionId);
    void PersistState(string partyId);
    List<GuestSession> GetAllGuests(string partyId);
    GuestSession? SetGuestCredits(string partyId, string sessionId, int credits);
    List<GuestSession> AdjustAllCredits(string partyId, int delta);
    bool RemoveGuest(string partyId, string sessionId);
    void UnmapSession(string sessionId);
    void EndParty(string partyId);
}
