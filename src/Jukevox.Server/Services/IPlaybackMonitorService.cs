using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public interface IPlaybackMonitorService
{
    void NotifyTrackStarted(string partyId, string trackUri);
    PlaybackStateDto? GetCachedPlaybackState(string partyId);
}
