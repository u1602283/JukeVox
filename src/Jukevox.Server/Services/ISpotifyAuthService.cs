using JukeVox.Server.Models;

namespace JukeVox.Server.Services;

public interface ISpotifyAuthService
{
    string GetAuthorizeUrl(string state);
    Task<SpotifyTokens?> ExchangeCodeAsync(string code);
    Task<string?> GetValidAccessTokenAsync();
}
