using JukeVox.Server.Models;

namespace JukeVox.Server.Services;

public interface ISpotifyAuthService
{
    string GetAuthorizeUrl(string partyId, string state);
    Task<SpotifyTokens?> ExchangeCodeAsync(string code, string partyId);
    Task<string?> GetValidAccessTokenAsync();
}
