using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using JukeVox.Server.Services;
using JukeVox.Server.Tests.Helpers;

namespace JukeVox.Server.Tests.Services;

[TestFixture]
public class SpotifyPlayerServiceTests
{
    private MockHttpHandler _handler = null!;
    private Mock<ISpotifyAuthService> _authService = null!;
    private SpotifyPlayerService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new MockHttpHandler();
        _authService = new Mock<ISpotifyAuthService>();
        _authService.Setup(a => a.GetValidAccessTokenAsync()).ReturnsAsync("test-token").Verifiable(Times.Once);

        _service = new SpotifyPlayerService(
            new HttpClient(_handler),
            _authService.Object,
            NullLogger<SpotifyPlayerService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _authService.VerifyAll();
        _authService.VerifyNoOtherCalls();
        _handler.Dispose();
    }

    private Uri RequestUri(int index = 0) => _handler.Requests[index].RequestUri!;

    // --- GetPlaybackStateAsync ---

    [Test]
    public async Task GetPlaybackStateAsync_Success_MapsAllFields()
    {
        _handler.EnqueueSuccess("""
        {
            "is_playing": true,
            "progress_ms": 45000,
            "item": {
                "uri": "spotify:track:abc123",
                "name": "Test Song",
                "duration_ms": 200000,
                "artists": [
                    { "name": "Artist One" },
                    { "name": "Artist Two" }
                ],
                "album": {
                    "name": "Test Album",
                    "images": [
                        { "url": "https://img.spotify.com/large.jpg", "height": 640, "width": 640 },
                        { "url": "https://img.spotify.com/small.jpg", "height": 64, "width": 64 }
                    ]
                }
            },
            "device": {
                "id": "device-1",
                "name": "My Speaker",
                "type": "Speaker",
                "is_active": true,
                "volume_percent": 75,
                "supports_volume": true
            }
        }
        """);

        var state = await _service.GetPlaybackStateAsync();

        state.Should().NotBeNull();
        state.Should().BeEquivalentTo(new
        {
            IsPlaying = true,
            TrackUri = "spotify:track:abc123",
            TrackName = "Test Song",
            ArtistName = "Artist One, Artist Two",
            AlbumName = "Test Album",
            AlbumImageUrl = "https://img.spotify.com/large.jpg",
            ProgressMs = 45000,
            DurationMs = 200000,
            VolumePercent = 75,
            SupportsVolume = true,
            DeviceId = "device-1",
            DeviceName = "My Speaker"
        });
    }

    [Test]
    public async Task GetPlaybackStateAsync_NoContent_ReturnsNull()
    {
        _handler.EnqueueNoContent();

        (await _service.GetPlaybackStateAsync()).Should().BeNull();
    }

    [Test]
    public async Task GetPlaybackStateAsync_Error_ReturnsNull()
    {
        _handler.EnqueueError(HttpStatusCode.InternalServerError);

        (await _service.GetPlaybackStateAsync()).Should().BeNull();
    }

    [Test]
    public async Task GetPlaybackStateAsync_NoToken_ReturnsNull()
    {
        _authService.Setup(a => a.GetValidAccessTokenAsync()).ReturnsAsync((string?)null).Verifiable(Times.Once);

        (await _service.GetPlaybackStateAsync()).Should().BeNull();
        _handler.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task GetPlaybackStateAsync_NullItem_ReturnsNullFields()
    {
        _handler.EnqueueSuccess("""
        {
            "is_playing": false,
            "progress_ms": null,
            "item": null,
            "device": null
        }
        """);

        var state = await _service.GetPlaybackStateAsync();

        state.Should().NotBeNull();
        state.Should().BeEquivalentTo(new
        {
            IsPlaying = false,
            TrackUri = (string?)null,
            TrackName = (string?)null,
            ArtistName = (string?)null,
            ProgressMs = 0,
            DurationMs = 0
        });
    }

    // --- PlayTrackAsync ---

    [Test]
    public async Task PlayTrackAsync_Success_ReturnsTrue()
    {
        _handler.EnqueueSuccess("{}");

        var result = await _service.PlayTrackAsync("spotify:track:abc");

        result.Should().BeTrue();
        RequestUri().AbsolutePath.Should().EndWith("/play");
    }

    [Test]
    public async Task PlayTrackAsync_WithDeviceId_IncludesQueryParam()
    {
        _handler.EnqueueSuccess("{}");

        await _service.PlayTrackAsync("spotify:track:abc", "device-123");

        var query = QueryHelpers.ParseQuery(RequestUri().Query);
        query["device_id"].ToString().Should().Be("device-123");
    }

    [Test]
    public async Task PlayTrackAsync_Failure_ReturnsFalse()
    {
        _handler.EnqueueError(HttpStatusCode.Forbidden);

        (await _service.PlayTrackAsync("spotify:track:abc")).Should().BeFalse();
    }

    // --- PauseAsync / ResumeAsync ---

    [Test]
    public async Task PauseAsync_Success_ReturnsTrue()
    {
        _handler.EnqueueSuccess("{}");

        (await _service.PauseAsync()).Should().BeTrue();
        RequestUri().AbsolutePath.Should().EndWith("/pause");
    }

    [Test]
    public async Task ResumeAsync_Success_ReturnsTrue()
    {
        _handler.EnqueueSuccess("{}");

        (await _service.ResumeAsync()).Should().BeTrue();
        RequestUri().AbsolutePath.Should().EndWith("/play");
    }

    // --- SkipNextAsync / SkipPreviousAsync ---

    [Test]
    public async Task SkipNextAsync_Success_ReturnsTrue()
    {
        _handler.EnqueueSuccess("{}");

        (await _service.SkipNextAsync()).Should().BeTrue();
        RequestUri().AbsolutePath.Should().EndWith("/next");
    }

    [Test]
    public async Task SkipPreviousAsync_Success_ReturnsTrue()
    {
        _handler.EnqueueSuccess("{}");

        (await _service.SkipPreviousAsync()).Should().BeTrue();
        RequestUri().AbsolutePath.Should().EndWith("/previous");
    }

    // --- SeekAsync ---

    [Test]
    public async Task SeekAsync_IncludesPositionInUrl()
    {
        _handler.EnqueueSuccess("{}");

        await _service.SeekAsync(30000);

        RequestUri().AbsolutePath.Should().EndWith("/seek");
        var query = QueryHelpers.ParseQuery(RequestUri().Query);
        query["position_ms"].ToString().Should().Be("30000");
    }

    // --- SetVolumeAsync ---

    [Test]
    public async Task SetVolumeAsync_IncludesPercentInUrl()
    {
        _handler.EnqueueSuccess("{}");

        await _service.SetVolumeAsync(50);

        RequestUri().AbsolutePath.Should().EndWith("/volume");
        var query = QueryHelpers.ParseQuery(RequestUri().Query);
        query["volume_percent"].ToString().Should().Be("50");
    }

    // --- GetDevicesAsync ---

    [Test]
    public async Task GetDevicesAsync_Success_MapsDevices()
    {
        _handler.EnqueueSuccess("""
        {
            "devices": [
                {
                    "id": "dev-1",
                    "name": "Living Room Speaker",
                    "type": "Speaker",
                    "is_active": true,
                    "volume_percent": 80,
                    "supports_volume": true
                },
                {
                    "id": "dev-2",
                    "name": "Phone",
                    "type": "Smartphone",
                    "is_active": false,
                    "volume_percent": null,
                    "supports_volume": false
                }
            ]
        }
        """);

        var devices = await _service.GetDevicesAsync();

        devices.Should().HaveCount(2);

        devices[0].Should().BeEquivalentTo(new
        {
            Id = "dev-1",
            Name = "Living Room Speaker",
            Type = "Speaker",
            IsActive = true,
            VolumePercent = 80,
            SupportsVolume = true
        });

        devices[1].Should().BeEquivalentTo(new
        {
            Id = "dev-2",
            IsActive = false,
            VolumePercent = 0, // null → 0
            SupportsVolume = false
        });
    }

    [Test]
    public async Task GetDevicesAsync_Error_ReturnsEmptyList()
    {
        _handler.EnqueueError();

        (await _service.GetDevicesAsync()).Should().BeEmpty();
    }

    // --- TransferPlaybackAsync ---

    [Test]
    public async Task TransferPlaybackAsync_Success_ReturnsTrue()
    {
        _handler.EnqueueSuccess("{}");

        var result = await _service.TransferPlaybackAsync("device-42");

        result.Should().BeTrue();
        _handler.Requests[0].Content.Should().NotBeNull();
    }

    // --- Bearer token ---

    [Test]
    public async Task AllRequests_SendBearerToken()
    {
        _handler.EnqueueSuccess("{}");

        await _service.PauseAsync();

        _handler.Requests[0].Headers.Authorization.Should().BeEquivalentTo(new
        {
            Scheme = "Bearer",
            Parameter = "test-token"
        });
    }
}
