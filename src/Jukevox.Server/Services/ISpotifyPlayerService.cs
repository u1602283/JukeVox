using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public interface ISpotifyPlayerService
{
    Task<PlaybackStateDto?> GetPlaybackStateAsync();
    Task<bool> PlayTrackAsync(string trackUri, string? deviceId = null);
    Task<bool> ResumeAsync();
    Task<bool> PauseAsync();
    Task<bool> SkipNextAsync();
    Task<bool> SkipPreviousAsync();
    Task<bool> SeekAsync(int positionMs);
    Task<bool> SetVolumeAsync(int percent);
    Task<List<SpotifyDeviceDto>> GetDevicesAsync();
    Task<bool> AddToQueueAsync(string trackUri);
    Task<List<string>> GetSpotifyQueueAsync();
    Task<bool> TransferPlaybackAsync(string deviceId);
}
