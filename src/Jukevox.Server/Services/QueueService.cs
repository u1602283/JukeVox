using JukeVox.Server.Models;
using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Services;

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

            // Insert before base playlist items so manual requests take priority
            var insertIndex = party.Queue.FindIndex(q => q.IsFromBasePlaylist);
            if (insertIndex >= 0)
                party.Queue.Insert(insertIndex, item);
            else
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
                IsFromBasePlaylist = q.IsFromBasePlaylist
            }).ToList();
        }
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
                IsFromBasePlaylist = true
            });
        }
    }
}
