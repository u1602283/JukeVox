using JukeVox.Server.Models;
using JukeVox.Server.Models.Dto;

namespace JukeVox.Server.Tests.Helpers;

public static class TestData
{
    public static AddToQueueRequest CreateAddToQueueRequest(string trackName = "Test Track") => new()
    {
        TrackUri = $"spotify:track:{Guid.NewGuid():N}",
        TrackName = trackName,
        ArtistName = "Test Artist",
        AlbumName = "Test Album",
        AlbumImageUrl = "https://example.com/image.jpg",
        DurationMs = 200000
    };

    public static QueueItem CreateQueueItem(string trackName = "Test Track", bool isFromBasePlaylist = false) => new()
    {
        TrackUri = $"spotify:track:{Guid.NewGuid():N}",
        TrackName = trackName,
        ArtistName = "Test Artist",
        AlbumName = "Test Album",
        AlbumImageUrl = "https://example.com/image.jpg",
        DurationMs = 200000,
        AddedBySessionId = "host-session",
        AddedByName = "Host",
        IsFromBasePlaylist = isFromBasePlaylist
    };

    public static GuestSession CreateGuestSession(string sessionId = "guest-1", string displayName = "Guest One", int credits = 5) => new()
    {
        SessionId = sessionId,
        DisplayName = displayName,
        CreditsRemaining = credits
    };

    public static BasePlaylistTrack CreateBasePlaylistTrack(string trackName = "Base Track") => new()
    {
        TrackUri = $"spotify:track:{Guid.NewGuid():N}",
        TrackName = trackName,
        ArtistName = "Base Artist",
        AlbumName = "Base Album",
        AlbumImageUrl = "https://example.com/base.jpg",
        DurationMs = 180000
    };

    public static Party CreateParty(string hostSessionId = "host-session", string inviteCode = "1234") => new()
    {
        InviteCode = inviteCode,
        HostSessionId = hostSessionId,
        DefaultCredits = 5
    };
}
