using JukeVox.Server.Models;
using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public interface IQueueService
{
    (QueueItem? Item, string? Error) AddToQueue(string sessionId, AddToQueueRequest request, bool isHost = false);
    bool RemoveFromQueue(string itemId);
    bool Reorder(List<string> orderedIds);
    QueueItem? Dequeue();
    QueueItem? SkipToPrevious();
    void SetBasePlaylist(List<BasePlaylistTrack> tracks, string playlistId, string playlistName);
    void ClearBasePlaylist();
    List<QueueItemDto> GetQueue();
    (bool Success, string? Error) Vote(string sessionId, string itemId, int vote, bool isHost = false);
    Dictionary<string, int> GetUserVotes(string sessionId);
}
