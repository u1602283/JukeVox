using System.Net;
using FluentAssertions;
using JukeVox.Server.Services;
using JukeVox.Server.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace JukeVox.Server.Tests.Services;

[TestFixture]
public class SpotifyPlaylistServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _handler = new MockHttpHandler();
        _authService = new Mock<ISpotifyAuthService>();
        _authService.Setup(a => a.GetValidAccessTokenAsync()).ReturnsAsync("test-token").Verifiable(Times.Once);

        _service = new SpotifyPlaylistService(
            new HttpClient(_handler),
            _authService.Object,
            NullLogger<SpotifyPlaylistService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _authService.VerifyAll();
        _authService.VerifyNoOtherCalls();
        _handler.Dispose();
    }

    private MockHttpHandler _handler = null!;
    private Mock<ISpotifyAuthService> _authService = null!;
    private SpotifyPlaylistService _service = null!;

    // --- GetUserPlaylistsAsync ---

    [Test]
    public async Task GetUserPlaylistsAsync_Success_MapsPlaylists()
    {
        _handler.EnqueueSuccess("""
                                {
                                    "items": [
                                        {
                                            "id": "pl-1",
                                            "name": "My Playlist",
                                            "images": [
                                                { "url": "https://img.spotify.com/playlist.jpg", "height": 300, "width": 300 }
                                            ],
                                            "tracks": { "total": 42 }
                                        },
                                        {
                                            "id": "pl-2",
                                            "name": "Empty Playlist",
                                            "images": [],
                                            "tracks": { "total": 0 }
                                        }
                                    ],
                                    "next": null,
                                    "total": 2
                                }
                                """);

        var playlists = await _service.GetUserPlaylistsAsync();

        playlists.Should().HaveCount(2);

        playlists[0]
            .Should()
            .BeEquivalentTo(new
            {
                Id = "pl-1",
                Name = "My Playlist",
                ImageUrl = "https://img.spotify.com/playlist.jpg",
                TrackCount = 42
            });

        playlists[1]
            .Should()
            .BeEquivalentTo(new
            {
                Id = "pl-2",
                ImageUrl = (string?)null, // empty images
                TrackCount = 0
            });
    }

    [Test]
    public async Task GetUserPlaylistsAsync_NullTracks_DefaultsToZero()
    {
        _handler.EnqueueSuccess("""
                                {
                                    "items": [
                                        {
                                            "id": "pl-1",
                                            "name": "No Tracks Ref",
                                            "images": [],
                                            "tracks": null
                                        }
                                    ],
                                    "total": 1
                                }
                                """);

        var playlists = await _service.GetUserPlaylistsAsync();

        playlists.Should()
            .ContainSingle()
            .Which.TrackCount.Should()
            .Be(0);
    }

    [Test]
    public async Task GetUserPlaylistsAsync_NoToken_ReturnsEmpty()
    {
        _authService.Setup(a => a.GetValidAccessTokenAsync()).ReturnsAsync((string?)null).Verifiable(Times.Once);

        (await _service.GetUserPlaylistsAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task GetUserPlaylistsAsync_Error_ReturnsEmpty()
    {
        _handler.EnqueueError();

        (await _service.GetUserPlaylistsAsync()).Should().BeEmpty();
    }

    // --- GetAllPlaylistTracksAsync ---

    [Test]
    public async Task GetAllPlaylistTracksAsync_SinglePage_ReturnsTracks()
    {
        _handler.EnqueueSuccess("""
                                {
                                    "items": [
                                        {
                                            "track": {
                                                "uri": "spotify:track:t1",
                                                "name": "Track One",
                                                "duration_ms": 210000,
                                                "artists": [{ "name": "Artist A" }],
                                                "album": {
                                                    "name": "Album X",
                                                    "images": [{ "url": "https://img.spotify.com/album.jpg" }]
                                                }
                                            },
                                            "is_local": false
                                        },
                                        {
                                            "track": {
                                                "uri": "spotify:track:t2",
                                                "name": "Track Two",
                                                "duration_ms": 180000,
                                                "artists": [{ "name": "Artist B" }, { "name": "Artist C" }],
                                                "album": { "name": "Album Y", "images": [] }
                                            },
                                            "is_local": false
                                        }
                                    ],
                                    "next": null,
                                    "total": 2
                                }
                                """);

        var tracks = await _service.GetAllPlaylistTracksAsync("playlist-id");

        tracks.Should().HaveCount(2);

        tracks[0]
            .Should()
            .BeEquivalentTo(new
            {
                TrackUri = "spotify:track:t1",
                TrackName = "Track One",
                ArtistName = "Artist A",
                AlbumName = "Album X",
                AlbumImageUrl = "https://img.spotify.com/album.jpg",
                DurationMs = 210000
            });

        tracks[1]
            .Should()
            .BeEquivalentTo(new
            {
                ArtistName = "Artist B, Artist C",
                AlbumImageUrl = (string?)null
            });
    }

    [Test]
    public async Task GetAllPlaylistTracksAsync_SkipsLocalTracks()
    {
        _handler.EnqueueSuccess("""
                                {
                                    "items": [
                                        {
                                            "track": {
                                                "uri": "spotify:track:remote",
                                                "name": "Remote Track",
                                                "duration_ms": 200000,
                                                "artists": [{ "name": "Artist" }],
                                                "album": { "name": "Album", "images": [] }
                                            },
                                            "is_local": false
                                        },
                                        {
                                            "track": {
                                                "uri": "spotify:local:artist:album:track:180",
                                                "name": "Local Track",
                                                "duration_ms": 180000,
                                                "artists": [{ "name": "Local Artist" }],
                                                "album": null
                                            },
                                            "is_local": true
                                        }
                                    ],
                                    "next": null,
                                    "total": 2
                                }
                                """);

        var tracks = await _service.GetAllPlaylistTracksAsync("playlist-id");

        tracks.Should()
            .ContainSingle()
            .Which.TrackName.Should()
            .Be("Remote Track");
    }

    [Test]
    public async Task GetAllPlaylistTracksAsync_SkipsNullTracks()
    {
        _handler.EnqueueSuccess("""
                                {
                                    "items": [
                                        {
                                            "track": null,
                                            "is_local": false
                                        },
                                        {
                                            "track": {
                                                "uri": "spotify:track:valid",
                                                "name": "Valid",
                                                "duration_ms": 150000,
                                                "artists": [{ "name": "A" }],
                                                "album": { "name": "B", "images": [] }
                                            },
                                            "is_local": false
                                        }
                                    ],
                                    "next": null,
                                    "total": 2
                                }
                                """);

        var tracks = await _service.GetAllPlaylistTracksAsync("playlist-id");

        tracks.Should()
            .ContainSingle()
            .Which.TrackName.Should()
            .Be("Valid");
    }

    [Test]
    public async Task GetAllPlaylistTracksAsync_Pagination_FollowsNextLink()
    {
        _authService.Setup(a => a.GetValidAccessTokenAsync()).ReturnsAsync("test-token").Verifiable(Times.Exactly(2));

        // Page 1
        _handler.EnqueueSuccess("""
                                {
                                    "items": [
                                        {
                                            "track": {
                                                "uri": "spotify:track:page1",
                                                "name": "Page 1 Track",
                                                "duration_ms": 200000,
                                                "artists": [{ "name": "A" }],
                                                "album": { "name": "Album", "images": [] }
                                            },
                                            "is_local": false
                                        }
                                    ],
                                    "next": "https://api.spotify.com/v1/playlists/id/tracks?offset=100&limit=100",
                                    "total": 2
                                }
                                """);

        // Page 2
        _handler.EnqueueSuccess("""
                                {
                                    "items": [
                                        {
                                            "track": {
                                                "uri": "spotify:track:page2",
                                                "name": "Page 2 Track",
                                                "duration_ms": 180000,
                                                "artists": [{ "name": "B" }],
                                                "album": { "name": "Album 2", "images": [] }
                                            },
                                            "is_local": false
                                        }
                                    ],
                                    "next": null,
                                    "total": 2
                                }
                                """);

        var tracks = await _service.GetAllPlaylistTracksAsync("playlist-id");

        tracks.Should().HaveCount(2);
        tracks.Select(t => t.TrackName).Should().Equal("Page 1 Track", "Page 2 Track");
        _handler.Requests.Should().HaveCount(2); // two HTTP calls
    }

    [Test]
    public async Task GetAllPlaylistTracksAsync_Error_ReturnsPartialResults()
    {
        _authService.Setup(a => a.GetValidAccessTokenAsync()).ReturnsAsync("test-token").Verifiable(Times.Exactly(2));

        // Page 1 succeeds
        _handler.EnqueueSuccess("""
                                {
                                    "items": [
                                        {
                                            "track": {
                                                "uri": "spotify:track:ok",
                                                "name": "OK Track",
                                                "duration_ms": 200000,
                                                "artists": [{ "name": "A" }],
                                                "album": { "name": "Album", "images": [] }
                                            },
                                            "is_local": false
                                        }
                                    ],
                                    "next": "https://api.spotify.com/v1/playlists/id/tracks?offset=100",
                                    "total": 200
                                }
                                """);

        // Page 2 fails
        _handler.EnqueueError(HttpStatusCode.InternalServerError);

        var tracks = await _service.GetAllPlaylistTracksAsync("playlist-id");

        tracks.Should()
            .ContainSingle()
            .Which.TrackName.Should()
            .Be("OK Track");
    }

    [Test]
    public async Task GetAllPlaylistTracksAsync_NoToken_ReturnsEmpty()
    {
        _authService.Setup(a => a.GetValidAccessTokenAsync()).ReturnsAsync((string?)null).Verifiable(Times.Once);

        (await _service.GetAllPlaylistTracksAsync("playlist-id")).Should().BeEmpty();
    }
}
