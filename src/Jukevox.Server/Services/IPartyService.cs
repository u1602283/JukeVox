using JukeVox.Server.Models;

namespace JukeVox.Server.Services;

public interface IPartyService
{
    Party? GetCurrentParty();
    Party CreateParty(string hostSessionId, string inviteCode, int defaultCredits);
    GuestSession? JoinParty(string sessionId, string inviteCode, string displayName);
    bool IsHost(string sessionId);
    bool IsParticipant(string sessionId);
    GuestSession? GetGuest(string sessionId);
    void UpdateSettings(string? inviteCode, int? defaultCredits);
    void SetSpotifyTokens(SpotifyTokens tokens);
    SpotifyTokens? GetSpotifyTokens();
    bool HasSavedParty();
    (string InviteCode, int QueueCount, int GuestCount, DateTime CreatedAt)? GetSavedPartySummary();
    Party? ResumeAsHost(string newHostSessionId);
    void PersistState();
    List<GuestSession> GetAllGuests();
    GuestSession? SetGuestCredits(string sessionId, int credits);
    List<GuestSession> AdjustAllCredits(int delta);
    bool RemoveGuest(string sessionId);
    void EndParty();
}
