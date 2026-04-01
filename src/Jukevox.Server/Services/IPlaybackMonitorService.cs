using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public interface IPlaybackMonitorService
{
    void NotifyTrackStarted(string partyId, string trackUri);
    void RecordHostActivity(string partyId);
    PlaybackStateDto? GetCachedPlaybackState(string partyId);
}
