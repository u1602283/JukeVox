using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Hubs;

public interface IPartyClient
{
    Task NowPlayingChanged(PlaybackStateDto? playbackState);
    Task PlaybackStateUpdated(PlaybackStateDto playbackState);
    Task QueueUpdated(List<QueueItemDto> queue);
    Task CreditsUpdated(int credits);
}
