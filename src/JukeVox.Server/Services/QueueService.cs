using System.Collections.Concurrent;
using JukeVox.Server.Models;
using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public class QueueService : IQueueService
{
    private readonly IPartyService _partyService;
    private readonly ConcurrentDictionary<string, Lock> _queueLocks = new();

    public QueueService(IPartyService partyService)
    {
        _partyService = partyService;
    }

    public (QueueItem? Item, string? Error) AddToQueue(string partyId,
        string sessionId,
        AddToQueueRequest request,
        bool isHost = false)
    {
        var party = _partyService.GetParty(partyId);
        if (party == null)
        {
            return (null, "No active party");
        }

        isHost = isHost || party.HostSessionId == sessionId;
        var addedByName = "Host";

        if (!isHost)
        {
            var (displayName, error) = _partyService.TrySpendCredit(partyId, sessionId);
            if (error != null)
            {
                return (null, error);
            }

            addedByName = displayName!;
        }

        lock (GetQueueLock(partyId))
        {
            var item = new QueueItem
            {
                TrackUri = request.TrackUri,
                TrackName = request.TrackName,
                ArtistName = request.ArtistName,
                AlbumName = request.AlbumName,
                AlbumImageUrl = request.AlbumImageUrl,
                DurationMs = request.DurationMs,
                AddedBySessionId = sessionId,
                AddedByName = addedByName,
                InsertionOrder = party.NextInsertionOrder++
            };

            // Remove matching base playlist items (promotes the track to a manual request)
            party.Queue.RemoveAll(q => q.IsFromBasePlaylist && TracksMatch(q, item));

            // Insert before base playlist items so manual requests take priority
            var insertIndex = party.Queue.FindIndex(q => q.IsFromBasePlaylist);
            if (insertIndex >= 0)
            {
                party.Queue.Insert(insertIndex, item);
            }
            else
            {
                party.Queue.Add(item);
            }

            SortQueue(party);
            _partyService.PersistState(partyId);
            return (item, null);
        }
    }

    public bool RemoveFromQueue(string partyId, string itemId)
    {
        lock (GetQueueLock(partyId))
        {
            var party = _partyService.GetParty(partyId);
            if (party == null)
            {
                return false;
            }

            var removed = party.Queue.RemoveAll(q => q.Id == itemId) > 0;
            if (removed)
            {
                _partyService.PersistState(partyId);
            }

            return removed;
        }
    }

    public bool Reorder(string partyId, List<string> orderedIds)
    {
        lock (GetQueueLock(partyId))
        {
            var party = _partyService.GetParty(partyId);
            if (party == null)
            {
                return false;
            }

            var lookup = party.Queue.ToDictionary(q => q.Id);
            var reordered = new List<QueueItem>();

            foreach (var id in orderedIds)
            {
                if (lookup.TryGetValue(id, out var item))
                {
                    reordered.Add(item);
                }
            }

            // Append any items not in the ordered list (safety)
            foreach (var item in party.Queue)
            {
                if (!orderedIds.Contains(item.Id))
                {
                    reordered.Add(item);
                }
            }

            party.Queue.Clear();
            party.Queue.AddRange(reordered);

            // Reassign insertion orders to reflect the host's chosen order
            for (var i = 0; i < party.Queue.Count; i++)
            {
                party.Queue[i].InsertionOrder = i;
            }

            party.NextInsertionOrder = party.Queue.Count;
            _partyService.PersistState(partyId);
            return true;
        }
    }

    public QueueItem? Dequeue(string partyId)
    {
        lock (GetQueueLock(partyId))
        {
            var party = _partyService.GetParty(partyId);
            if (party == null || party.Queue.Count == 0)
            {
                return null;
            }

            var next = party.Queue[0];
            party.Queue.RemoveAt(0);

            if (party.CurrentTrack != null)
            {
                party.PlaybackHistory.Add(party.CurrentTrack);
            }

            party.CurrentTrack = next;

            // Auto-refill when queue is empty and a base playlist is configured
            if (party.Queue.Count == 0 && party.BasePlaylistTracks.Count > 0)
            {
                RefillFromBasePlaylist(party);
            }

            _partyService.PersistState(partyId);
            return next;
        }
    }

    public QueueItem? SkipToPrevious(string partyId)
    {
        lock (GetQueueLock(partyId))
        {
            var party = _partyService.GetParty(partyId);
            if (party == null || party.PlaybackHistory.Count == 0)
            {
                return null;
            }

            // Put the current track back at the front of the queue
            if (party.CurrentTrack != null)
            {
                party.CurrentTrack.Id = Guid.NewGuid().ToString("N")[..8];
                party.Queue.Insert(0, party.CurrentTrack);
            }

            var prev = party.PlaybackHistory[^1];
            party.PlaybackHistory.RemoveAt(party.PlaybackHistory.Count - 1);
            party.CurrentTrack = prev;

            _partyService.PersistState(partyId);
            return prev;
        }
    }

    public void SetBasePlaylist(string partyId, List<BasePlaylistTrack> tracks, string playlistId, string playlistName)
    {
        lock (GetQueueLock(partyId))
        {
            var party = _partyService.GetParty(partyId);
            if (party == null)
            {
                return;
            }

            // Remove existing base playlist items from queue
            party.Queue.RemoveAll(q => q.IsFromBasePlaylist);

            party.BasePlaylistId = playlistId;
            party.BasePlaylistName = playlistName;
            party.BasePlaylistTracks = tracks;

            RefillFromBasePlaylist(party);
            _partyService.PersistState(partyId);
        }
    }

    public void ClearBasePlaylist(string partyId)
    {
        lock (GetQueueLock(partyId))
        {
            var party = _partyService.GetParty(partyId);
            if (party == null)
            {
                return;
            }

            party.Queue.RemoveAll(q => q.IsFromBasePlaylist);
            party.BasePlaylistId = null;
            party.BasePlaylistName = null;
            party.BasePlaylistTracks = [];

            _partyService.PersistState(partyId);
        }
    }

    public List<QueueItemDto> GetQueue(string partyId)
    {
        var party = _partyService.GetParty(partyId);
        if (party == null)
        {
            return [];
        }

        lock (GetQueueLock(partyId))
        {
            MigrateInsertionOrders(party);

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
                    AddedAt = q.AddedAt,
                    IsFromBasePlaylist = q.IsFromBasePlaylist,
                    Score = q.Score
                })
                .ToList();
        }
    }

    public (bool Success, string? Error) Vote(string partyId,
        string sessionId,
        string itemId,
        int vote,
        bool isHost = false)
    {
        lock (GetQueueLock(partyId))
        {
            var party = _partyService.GetParty(partyId);
            if (party == null)
            {
                return (false, "No active party");
            }

            if (vote is not (-1 or 0 or 1))
            {
                return (false, "Vote must be -1, 0, or 1");
            }

            isHost = isHost || party.HostSessionId == sessionId;
            if (!isHost && !party.Guests.ContainsKey(sessionId))
            {
                return (false, "Not a party participant");
            }

            var item = party.Queue.Find(q => q.Id == itemId);
            if (item == null)
            {
                return (false, "Item not found");
            }

            var wasPromoted = item.Score >= 3;

            if (vote == 0)
            {
                item.Votes.Remove(sessionId);
            }
            else
            {
                item.Votes[sessionId] = vote;
            }

            var isPromoted = item.Score >= 3;

            // Auto-remove items at -3 or below
            if (item.Score <= -3)
            {
                party.Queue.Remove(item);
            }
            else if (wasPromoted != isPromoted)
            {
                // Only re-sort when crossing the promotion threshold
                SortQueue(party);
            }

            _partyService.PersistState(partyId);
            return (true, null);
        }
    }

    public Dictionary<string, int> GetUserVotes(string partyId, string sessionId)
    {
        var party = _partyService.GetParty(partyId);
        if (party == null)
        {
            return new Dictionary<string, int>();
        }

        lock (GetQueueLock(partyId))
        {
            var votes = new Dictionary<string, int>();
            foreach (var item in party.Queue)
            {
                if (item.Votes.TryGetValue(sessionId, out var v))
                {
                    votes[item.Id] = v;
                }
            }

            return votes;
        }
    }

    private Lock GetQueueLock(string partyId) => _queueLocks.GetOrAdd(partyId, _ => new Lock());

    private static bool TracksMatch(QueueItem a, QueueItem b)
    {
        if (a.TrackUri == b.TrackUri)
        {
            return true;
        }

        return string.Equals(a.TrackName, b.TrackName, StringComparison.OrdinalIgnoreCase)
               && string.Equals(a.ArtistName, b.ArtistName, StringComparison.OrdinalIgnoreCase);
    }

    private static void SortQueue(Party party)
    {
        var sorted = party.Queue.ToList();

        sorted.Sort((a, b) =>
        {
            static int Tier(QueueItem x)
            {
                if (x.Score >= 3)
                {
                    return 0;
                }

                if (x.IsFromBasePlaylist)
                {
                    return 2;
                }

                return 1;
            }

            var tierCmp = Tier(a).CompareTo(Tier(b));
            if (tierCmp != 0)
            {
                return tierCmp;
            }

            if (Tier(a) == 0)
            {
                if (a.Score != b.Score)
                {
                    return b.Score.CompareTo(a.Score);
                }

                return a.InsertionOrder.CompareTo(b.InsertionOrder);
            }

            return a.InsertionOrder.CompareTo(b.InsertionOrder);
        });

        party.Queue.Clear();
        party.Queue.AddRange(sorted);
    }

    private static void MigrateInsertionOrders(Party party)
    {
        if (party.Queue.Count <= 1)
        {
            return;
        }

        if (party.Queue.Any(q => q.InsertionOrder != 0))
        {
            return;
        }

        for (var i = 0; i < party.Queue.Count; i++)
        {
            party.Queue[i].InsertionOrder = i;
        }

        party.NextInsertionOrder = party.Queue.Count;
    }

    private static void RefillFromBasePlaylist(Party party)
    {
        var shuffled = party.BasePlaylistTracks.ToArray();
        Random.Shared.Shuffle(shuffled);

        foreach (var track in shuffled)
        {
            party.Queue.Add(new QueueItem
            {
                TrackUri = track.TrackUri,
                TrackName = track.TrackName,
                ArtistName = track.ArtistName,
                AlbumName = track.AlbumName,
                AlbumImageUrl = track.AlbumImageUrl,
                DurationMs = track.DurationMs,
                AddedBySessionId = party.HostSessionId,
                AddedByName = "Base Playlist",
                IsFromBasePlaylist = true,
                InsertionOrder = party.NextInsertionOrder++
            });
        }
    }
}
