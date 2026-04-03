using System.Net;
using FluentAssertions;
using JukeVox.Server.Services;
using JukeVox.Server.Tests.Helpers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace JukeVox.Server.Tests.Services;

[TestFixture]
public class SpotifySearchServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _handler = new MockHttpHandler();
        _authService = new Mock<ISpotifyAuthService>();
        _authService.Setup(a => a.GetValidAccessTokenAsync()).ReturnsAsync("test-token").Verifiable(Times.Once);

        _service = new SpotifySearchService(
            new HttpClient(_handler),
            _authService.Object,
            NullLogger<SpotifySearchService>.Instance);
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
    private SpotifySearchService _service = null!;

    [Test]
    public async Task SearchAsync_Success_MapsResults()
    {
        _handler.EnqueueSuccess("""
                                {
                                    "tracks": {
                                        "items": [
                                            {
                                                "uri": "spotify:track:aaa",
                                                "name": "Song A",
                                                "duration_ms": 210000,
                                                "artists": [
                                                    { "name": "Artist X" }
                                                ],
                                                "album": {
                                                    "name": "Album One",
                                                    "images": [
                                                        { "url": "https://img.spotify.com/a.jpg", "height": 640, "width": 640 }
                                                    ]
                                                }
                                            },
                                            {
                                                "uri": "spotify:track:bbb",
                                                "name": "Song B",
                                                "duration_ms": 180000,
                                                "artists": [
                                                    { "name": "Artist Y" },
                                                    { "name": "Artist Z" }
                                                ],
                                                "album": {
                                                    "name": "Album Two",
                                                    "images": []
                                                }
                                            }
                                        ]
                                    }
                                }
                                """);

        var results = await _service.SearchAsync("test query", 10);

        results.Should().HaveCount(2);

        results[0]
            .Should()
            .BeEquivalentTo(new
            {
                TrackUri = "spotify:track:aaa",
                TrackName = "Song A",
                ArtistName = "Artist X",
                AlbumName = "Album One",
                AlbumImageUrl = "https://img.spotify.com/a.jpg",
                DurationMs = 210000
            });

        results[1]
            .Should()
            .BeEquivalentTo(new
            {
                ArtistName = "Artist Y, Artist Z",
                AlbumImageUrl = (string?)null // empty images list
            });
    }

    [Test]
    public async Task SearchAsync_UrlContainsQueryAndLimit()
    {
        _handler.EnqueueSuccess("""{"tracks":{"items":[]}}""");

        await _service.SearchAsync("hello world", 5);

        var uri = _handler.Requests[0].RequestUri!;
        var query = QueryHelpers.ParseQuery(uri.Query);
        query["q"].ToString().Should().Be("hello world");
        query["type"].ToString().Should().Be("track");
        query["limit"].ToString().Should().Be("5");
    }

    [Test]
    public async Task SearchAsync_NoToken_ReturnsEmptyWithoutHttpCall()
    {
        _authService.Setup(a => a.GetValidAccessTokenAsync()).ReturnsAsync((string?)null).Verifiable(Times.Once);

        var results = await _service.SearchAsync("query");

        results.Should().BeEmpty();
        _handler.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task SearchAsync_HttpError_ReturnsEmpty()
    {
        _handler.EnqueueError(HttpStatusCode.InternalServerError);

        (await _service.SearchAsync("query")).Should().BeEmpty();
    }

    [Test]
    public async Task SearchAsync_NullTracksInResponse_ReturnsEmpty()
    {
        _handler.EnqueueSuccess("""{"tracks":null}""");

        (await _service.SearchAsync("query")).Should().BeEmpty();
    }

    [Test]
    public async Task SearchAsync_NullAlbum_DefaultsAlbumNameToEmpty()
    {
        _handler.EnqueueSuccess("""
                                {
                                    "tracks": {
                                        "items": [
                                            {
                                                "uri": "spotify:track:abc",
                                                "name": "No Album Song",
                                                "duration_ms": 120000,
                                                "artists": [{ "name": "Solo" }],
                                                "album": null
                                            }
                                        ]
                                    }
                                }
                                """);

        var results = await _service.SearchAsync("query");

        results.Should().ContainSingle();
        results[0].AlbumName.Should().BeEmpty();
        results[0].AlbumImageUrl.Should().BeNull();
    }

    [Test]
    public async Task SearchAsync_SendsBearerToken()
    {
        _handler.EnqueueSuccess("""{"tracks":{"items":[]}}""");

        await _service.SearchAsync("query");

        var auth = _handler.Requests[0].Headers.Authorization;
        auth!.Scheme.Should().Be("Bearer");
        auth.Parameter.Should().Be("test-token");
    }
}
