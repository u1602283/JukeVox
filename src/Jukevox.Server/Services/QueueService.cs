using JukeVox.Server.Models;
using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

public class QueueService : IQueueService
{
    private readonly IPartyService _partyService;
    private readonly Lock _lock = new();

    public QueueService(IPartyService partyService)
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
                AddedByName = addedByName,
                InsertionOrder = party.NextInsertionOrder++
            };

            // Insert before base playlist items so manual requests take priority
            var insertIndex = party.Queue.FindIndex(q => q.IsFromBasePlaylist);
            if (insertIndex >= 0)
                party.Queue.Insert(insertIndex, item);
            else
                party.Queue.Add(item);

            SortQueue(party);
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

            // Reassign insertion orders to match new positions so votes offset from new anchors
            for (int i = 0; i < party.Queue.Count; i++)
                party.Queue[i].InsertionOrder = i;
            party.NextInsertionOrder = party.Queue.Count;

            SortQueue(party);
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

            if (party.CurrentTrack != null)
                party.PlaybackHistory.Add(party.CurrentTrack);
            party.CurrentTrack = next;

            // Auto-refill when queue is empty and a base playlist is configured
            if (party.Queue.Count == 0 && party.BasePlaylistTracks.Count > 0)
            {
                RefillFromBasePlaylist(party);
            }

            _partyService.PersistState();
            return next;
        }
    }

    /// <summary>
    /// Returns the previously played track and requeues the currently playing track
    /// at the front of the queue. Returns null if there is no history.
    /// </summary>
    public QueueItem? SkipToPrevious()
    {
        lock (_lock)
        {
            var party = _partyService.GetCurrentParty();
            if (party == null || party.PlaybackHistory.Count == 0) return null;

            // Put the current track back at the front of the queue
            if (party.CurrentTrack != null)
            {
                party.CurrentTrack.Id = Guid.NewGuid().ToString("N")[..8];
                party.Queue.Insert(0, party.CurrentTrack);
            }

            var prev = party.PlaybackHistory[^1];
            party.PlaybackHistory.RemoveAt(party.PlaybackHistory.Count - 1);
            party.CurrentTrack = prev;

            _partyService.PersistState();
            return prev;
        }
    }

    public void SetBasePlaylist(List<BasePlaylistTrack> tracks, string playlistId, string playlistName)
    {
        lock (_lock)
        {
            var party = _partyService.GetCurrentParty();
            if (party == null) return;

            // Remove existing base playlist items from queue
            party.Queue.RemoveAll(q => q.IsFromBasePlaylist);

            party.BasePlaylistId = playlistId;
            party.BasePlaylistName = playlistName;
            party.BasePlaylistTracks = tracks;

            RefillFromBasePlaylist(party);
            _partyService.PersistState();
        }
    }

    public void ClearBasePlaylist()
    {
        lock (_lock)
        {
            var party = _partyService.GetCurrentParty();
            if (party == null) return;

            party.Queue.RemoveAll(q => q.IsFromBasePlaylist);
            party.BasePlaylistId = null;
            party.BasePlaylistName = null;
            party.BasePlaylistTracks = [];

            _partyService.PersistState();
        }
    }

    public List<QueueItemDto> GetQueue()
    {
        lock (_lock)
        {
            var party = _partyService.GetCurrentParty();
            if (party == null) return [];

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
            }).ToList();
        }
    }

    public (bool Success, string? Error) Vote(string sessionId, string itemId, int vote)
    {
        lock (_lock)
        {
            var party = _partyService.GetCurrentParty();
            if (party == null) return (false, "No active party");

            if (vote is not (-1 or 0 or 1))
                return (false, "Vote must be -1, 0, or 1");

            bool isHost = party.HostSessionId == sessionId;
            if (!isHost && !party.Guests.ContainsKey(sessionId))
                return (false, "Not a party participant");

            var item = party.Queue.Find(q => q.Id == itemId);
            if (item == null) return (false, "Item not found");

            if (vote == 0)
                item.Votes.Remove(sessionId);
            else
                item.Votes[sessionId] = vote;

            // Auto-remove items at -3 or below
            if (item.Score <= -3)
            {
                party.Queue.Remove(item);
            }
            else
            {
                SortQueue(party);
            }

            _partyService.PersistState();
            return (true, null);
        }
    }

    public Dictionary<string, int> GetUserVotes(string sessionId)
    {
        lock (_lock)
        {
            var party = _partyService.GetCurrentParty();
            if (party == null) return new();

            var votes = new Dictionary<string, int>();
            foreach (var item in party.Queue)
            {
                if (item.Votes.TryGetValue(sessionId, out var v))
                    votes[item.Id] = v;
            }
            return votes;
        }
    }

    private static void SortQueue(Party party)
    {
        var sorted = party.Queue.ToList();

        sorted.Sort((a, b) =>
        {
            // Only upvotes (positive score) shift position; downvotes don't move items down
            var aKey = a.InsertionOrder - Math.Max(a.Score, 0);
            var bKey = b.InsertionOrder - Math.Max(b.Score, 0);
            if (aKey != bKey) return aKey.CompareTo(bKey);
            if (a.Score != b.Score) return b.Score.CompareTo(a.Score); // higher score wins
            return a.InsertionOrder.CompareTo(b.InsertionOrder); // lower insertion order wins
        });

        party.Queue.Clear();
        party.Queue.AddRange(sorted);
    }

    private static void MigrateInsertionOrders(Party party)
    {
        if (party.Queue.Count <= 1) return;
        if (party.Queue.Any(q => q.InsertionOrder != 0)) return;

        // All items have InsertionOrder == 0 — backfill from list position
        for (int i = 0; i < party.Queue.Count; i++)
            party.Queue[i].InsertionOrder = i;
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
