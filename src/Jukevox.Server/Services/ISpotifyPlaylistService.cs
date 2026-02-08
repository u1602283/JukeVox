using JukeVox.Server.Models;
using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public interface ISpotifyPlaylistService
{
    Task<List<SpotifyPlaylistDto>> GetUserPlaylistsAsync(int limit = 50, int offset = 0);
    Task<List<BasePlaylistTrack>> GetAllPlaylistTracksAsync(string playlistId);
}
