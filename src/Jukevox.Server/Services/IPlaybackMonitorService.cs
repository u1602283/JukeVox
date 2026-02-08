namespace JukeVox.Server.Services;

public interface IPlaybackMonitorService
{
    void NotifyTrackStarted(string trackUri);
}
