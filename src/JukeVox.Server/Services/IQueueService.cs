using JukeVox.Server.Models;
using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public interface IQueueService
{
    (QueueItem? Item, string? Error) AddToQueue(string partyId,
        string sessionId,
        AddToQueueRequest request,
        bool isHost = false);

    bool RemoveFromQueue(string partyId, string itemId);
    bool Reorder(string partyId, List<string> orderedIds);
    QueueItem? Dequeue(string partyId);
    QueueItem? SkipToPrevious(string partyId);
    void SetBasePlaylist(string partyId, List<BasePlaylistTrack> tracks, string playlistId, string playlistName);
    void ClearBasePlaylist(string partyId);
    List<QueueItemDto> GetQueue(string partyId);
    (bool Success, string? Error) Vote(string partyId, string sessionId, string itemId, int vote, bool isHost = false);
    Dictionary<string, int> GetUserVotes(string partyId, string sessionId);
}
