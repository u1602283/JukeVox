using Jukevox.Server.Models;
using Jukevox.Server.Models.Dto;

namespace Jukevox.Server.Services;

public class QueueService
{
    private readonly PartyService _partyService;
    private readonly Lock _lock = new();

    public QueueService(PartyService partyService)
    {
        _partyService = partyService;
    }

    public (QueueItem? Item, string? Error) AddToQueue(string sessionId, AddToQueueRequest request)
    {
        lock (_lock)
        {
            var party = _partyService.GetCurrentParty();
            if (party == null) return (null, "No active party");

            bool isHost = party.HostSessionId == sessionId;
            string addedByName = "Host";

            if (!isHost)
            {
                if (!party.Guests.TryGetValue(sessionId, out var guest))
                    return (null, "Not a party participant");

                if (guest.CreditsRemaining <= 0)
                    return (null, "No credits remaining");

                guest.CreditsRemaining--;
                addedByName = guest.DisplayName;
            }

            var item = new QueueItem
            {
                TrackUri = request.TrackUri,
                TrackName = request.TrackName,
                ArtistName = request.ArtistName,
                AlbumName = request.AlbumName,
                AlbumImageUrl = request.AlbumImageUrl,
                DurationMs = request.DurationMs,
                AddedBySessionId = sessionId,
                AddedByName = addedByName
            };

            party.Queue.Add(item);
            _partyService.PersistState();
            return (item, null);
        }
    }

    public bool RemoveFromQueue(string itemId)
    {
        lock (_lock)
        {
            var party = _partyService.GetCurrentParty();
            if (party == null) return false;
            var removed = party.Queue.RemoveAll(q => q.Id == itemId) > 0;
            if (removed) _partyService.PersistState();
            return removed;
        }
    }

    public bool Reorder(List<string> orderedIds)
    {
        lock (_lock)
        {
            var party = _partyService.GetCurrentParty();
            if (party == null) return false;

            var lookup = party.Queue.ToDictionary(q => q.Id);
            var reordered = new List<QueueItem>();

            foreach (var id in orderedIds)
            {
                if (lookup.TryGetValue(id, out var item))
                    reordered.Add(item);
            }

            // Append any items not in the ordered list (safety)
            foreach (var item in party.Queue)
            {
                if (!orderedIds.Contains(item.Id))
                    reordered.Add(item);
            }

            party.Queue.Clear();
            party.Queue.AddRange(reordered);
            _partyService.PersistState();
            return true;
        }
    }

    public QueueItem? Dequeue()
    {
        lock (_lock)
        {
            var party = _partyService.GetCurrentParty();
            if (party == null || party.Queue.Count == 0) return null;

            var next = party.Queue[0];
            party.Queue.RemoveAt(0);
            _partyService.PersistState();
            return next;
        }
    }

    public List<QueueItemDto> GetQueue()
    {
        lock (_lock)
        {
            var party = _partyService.GetCurrentParty();
            if (party == null) return [];

            return party.Queue.Select(q => new QueueItemDto
            {
                Id = q.Id,
                TrackUri = q.TrackUri,
                TrackName = q.TrackName,
                ArtistName = q.ArtistName,
                AlbumName = q.AlbumName,
                AlbumImageUrl = q.AlbumImageUrl,
                DurationMs = q.DurationMs,
                AddedByName = q.AddedByName,
                AddedAt = q.AddedAt
            }).ToList();
        }
    }
}
