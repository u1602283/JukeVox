using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public interface IPlaybackMonitorService
{
    void NotifyTrackStarted(string trackUri);
    PlaybackStateDto? GetCachedPlaybackState();
}
