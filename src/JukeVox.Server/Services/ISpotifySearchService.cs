using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public interface ISpotifySearchService
{
    Task<List<SearchResultDto>> SearchAsync(string query, int limit = 20);
}
