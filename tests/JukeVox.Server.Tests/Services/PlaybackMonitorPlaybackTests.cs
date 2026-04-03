using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using JukeVox.Server.Configuration;
using JukeVox.Server.Hubs;
using JukeVox.Server.Models;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Services;
using JukeVox.Server.Tests.Helpers;

namespace JukeVox.Server.Tests.Services;

[TestFixture]
public class PlaybackMonitorPlaybackTests
{
    private Mock<ISpotifyPlayerService> _playerService = null!;
    private Mock<IQueueService> _queueService = null!;
    private Mock<IPartyService> _partyService = null!;
    private MockHubContext _hub = null!;
    private PlaybackMonitorService _monitor = null!;
    private ServiceProvider _serviceProvider = null!;
    private FakeTimeProvider _time = null!;
    private Party _party = null!;

    [SetUp]
    public void SetUp()
    {
        _playerService = new Mock<ISpotifyPlayerService>();
        _queueService = new Mock<IQueueService>();
        _partyService = new Mock<IPartyService>();
        _hub = new MockHubContext();
        _time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        _hub.PartyClient.Setup(c => c.PlaybackStateUpdated(It.IsAny<PlaybackStateDto>())).Returns(Task.CompletedTask);
        _hub.PartyClient.Setup(c => c.NowPlayingChanged(It.IsAny<PlaybackStateDto>())).Returns(Task.CompletedTask);
        _hub.PartyClient.Setup(c => c.QueueUpdated(It.IsAny<List<QueueItemDto>>())).Returns(Task.CompletedTask);
        _hub.PartyClient.Setup(c => c.PartyEnded()).Returns(Task.CompletedTask);
        _hub.PartyClient.Setup(c => c.PartySleeping()).Returns(Task.CompletedTask);
        _hub.PartyClient.Setup(c => c.PartyWoke()).Returns(Task.CompletedTask);
        _hub.PartyClient.Setup(c => c.CreditsUpdated(It.IsAny<int>())).Returns(Task.CompletedTask);

        _party = TestData.CreateParty();
        _party.SpotifyTokens = new SpotifyTokens
        {
            AccessToken = "tok",
            RefreshToken = "ref",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        _partyService.Setup(p => p.GetAllParties()).Returns([_party]);
        _partyService.Setup(p => p.GetParty(_party.Id)).Returns(_party);

        _queueService.Setup(q => q.GetQueue(_party.Id)).Returns([]);

        var services = new ServiceCollection();
        services.AddScoped(_ => _partyService.Object);
        services.AddScoped(_ => _playerService.Object);
        services.AddScoped(_ => _queueService.Object);
        services.AddSingleton(_hub.HubContext.Object);
        services.AddScoped<IPartyContextAccessor, PartyContextAccessor>();
        _serviceProvider = services.BuildServiceProvider();

        var options = Options.Create(new PartyInactivityOptions
        {
            SleepAfterMinutes = 9999,
            AutoEndAfterMinutes = 9999
        });

        _monitor = new PlaybackMonitorService(
            _serviceProvider,
            NullLogger<PlaybackMonitorService>.Instance,
            options,
            _time);
    }

    [TearDown]
    public void TearDown()
    {
        _monitor.Dispose();
        _serviceProvider.Dispose();
    }

    /// <summary>
    /// Starts the monitor background service and advances fake time to run
    /// the specified number of poll cycles, then stops the service.
    /// Each cycle = one poll + one 2-second delay.
    /// </summary>
    private async Task RunCycles(int count)
    {
        using var cts = new CancellationTokenSource();
        await _monitor.StartAsync(cts.Token);

        // Let the first poll execute
        await Task.Delay(50);

        for (var i = 1; i < count; i++)
        {
            // Advance past the 2-second delay to trigger the next poll
            _time.Advance(TimeSpan.FromSeconds(3));
            await Task.Delay(50);
        }

        await cts.CancelAsync();
        await _monitor.StopAsync(CancellationToken.None);
    }

    private static PlaybackStateDto MakeState(string trackUri, bool isPlaying,
        int progressMs = 50000, int durationMs = 200000, string? deviceId = null) => new()
    {
        TrackUri = trackUri,
        TrackName = "Track",
        ArtistName = "Artist",
        AlbumName = "Album",
        IsPlaying = isPlaying,
        ProgressMs = progressMs,
        DurationMs = durationMs,
        DeviceId = deviceId ?? "dev-1",
        DeviceName = "Speaker"
    };

    private static QueueItemDto MakeQueueItemDto(string trackUri = "x") => new()
    {
        Id = "item-1", TrackUri = trackUri, TrackName = "x",
        ArtistName = "x", AlbumName = "x", AddedByName = "Host", DurationMs = 200000
    };

    // --- Normal playback broadcast ---

    [Test]
    public async Task Poll_NormalPlayback_BroadcastsPlaybackStateUpdated()
    {
        _playerService.Setup(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true));

        await RunCycles(1);

        _hub.PartyClient.Verify(c => c.PlaybackStateUpdated(It.IsAny<PlaybackStateDto>()), Times.AtLeastOnce);
    }

    [Test]
    public async Task Poll_FirstTrack_BroadcastsNowPlayingChanged()
    {
        _playerService.Setup(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true));

        await RunCycles(1);

        _hub.PartyClient.Verify(c => c.NowPlayingChanged(It.Is<PlaybackStateDto>(
            s => s.TrackUri == "spotify:track:a")), Times.AtLeastOnce);
    }

    [Test]
    public async Task Poll_CurrentTrack_SetsAddedByNameAndIsFromBasePlaylist()
    {
        _party.CurrentTrack = TestData.CreateQueueItem("My Song", isFromBasePlaylist: true);
        _party.CurrentTrack.AddedByName = "Alice";

        _playerService.Setup(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState(_party.CurrentTrack.TrackUri, isPlaying: true));

        _monitor.NotifyTrackStarted(_party.Id, _party.CurrentTrack.TrackUri);

        await RunCycles(1);

        _hub.PartyClient.Verify(c => c.PlaybackStateUpdated(It.Is<PlaybackStateDto>(
            s => s.AddedByName == "Alice" && s.IsFromBasePlaylist)), Times.AtLeastOnce);
    }

    // --- Track end detection ---

    [Test]
    public async Task Poll_TrackFinished_NearEnd_DequeuesNext()
    {
        var trackUri = "spotify:track:a";
        var nextItem = TestData.CreateQueueItem("Next");

        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState(trackUri, isPlaying: true, progressMs: 196000, durationMs: 200000))
            .ReturnsAsync(MakeState(trackUri, isPlaying: false, progressMs: 200000, durationMs: 200000));

        _queueService.Setup(q => q.Dequeue(_party.Id)).Returns(nextItem);
        _playerService.Setup(p => p.PlayTrackAsync(nextItem.TrackUri, It.IsAny<string?>())).ReturnsAsync(true);

        await RunCycles(2);

        _queueService.Verify(q => q.Dequeue(_party.Id), Times.AtLeastOnce);
    }

    [Test]
    public async Task Poll_TrackFinished_ProgressReset_DequeuesNext()
    {
        var trackUri = "spotify:track:a";
        var nextItem = TestData.CreateQueueItem("Next");

        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState(trackUri, isPlaying: true, progressMs: 150000, durationMs: 200000))
            .ReturnsAsync(MakeState(trackUri, isPlaying: false, progressMs: 0, durationMs: 200000));

        _queueService.Setup(q => q.Dequeue(_party.Id)).Returns(nextItem);
        _playerService.Setup(p => p.PlayTrackAsync(nextItem.TrackUri, It.IsAny<string?>())).ReturnsAsync(true);

        await RunCycles(2);

        _queueService.Verify(q => q.Dequeue(_party.Id), Times.AtLeastOnce);
    }

    [Test]
    public async Task Poll_TrackPaused_MidTrack_DoesNotAdvance()
    {
        var trackUri = "spotify:track:a";

        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState(trackUri, isPlaying: true, progressMs: 50000, durationMs: 200000))
            .ReturnsAsync(MakeState(trackUri, isPlaying: false, progressMs: 50000, durationMs: 200000));

        await RunCycles(2);

        _queueService.Verify(q => q.Dequeue(It.IsAny<string>()), Times.Never);
    }

    // --- Null playback state ---

    [Test]
    public async Task Poll_PlaybackNull_WasPlaying_Advances()
    {
        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true))
            .ReturnsAsync((PlaybackStateDto?)null);

        _queueService.Setup(q => q.Dequeue(_party.Id)).Returns(TestData.CreateQueueItem());
        _playerService.Setup(p => p.PlayTrackAsync(It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(true);

        await RunCycles(2);

        _queueService.Verify(q => q.Dequeue(_party.Id), Times.AtLeastOnce);
    }

    [Test]
    public async Task Poll_PlaybackNull_WasNotPlaying_DoesNotAdvance()
    {
        _playerService.Setup(p => p.GetPlaybackStateAsync())
            .ReturnsAsync((PlaybackStateDto?)null);

        await RunCycles(1);

        _queueService.Verify(q => q.Dequeue(It.IsAny<string>()), Times.Never);
    }

    // --- Foreign track detection ---

    [Test]
    public async Task Poll_ForeignTrack_OutsideGracePeriod_WithQueue_Advances()
    {
        var nextItem = TestData.CreateQueueItem("Next");

        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true))
            .ReturnsAsync(MakeState("spotify:track:b", isPlaying: true));

        _monitor.NotifyTrackStarted(_party.Id, "spotify:track:a");
        // Advance past 5s grace period
        _time.Advance(TimeSpan.FromSeconds(6));

        _queueService.Setup(q => q.GetQueue(_party.Id)).Returns([MakeQueueItemDto()]);
        _queueService.Setup(q => q.Dequeue(_party.Id)).Returns(nextItem);
        _playerService.Setup(p => p.PlayTrackAsync(nextItem.TrackUri, It.IsAny<string?>())).ReturnsAsync(true);

        await RunCycles(2);

        _queueService.Verify(q => q.Dequeue(_party.Id), Times.AtLeastOnce);
    }

    [Test]
    public async Task Poll_ForeignTrack_OutsideGracePeriod_EmptyQueue_StopsTracking()
    {
        _monitor.NotifyTrackStarted(_party.Id, "spotify:track:a");
        _time.Advance(TimeSpan.FromSeconds(6));

        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true))
            .ReturnsAsync(MakeState("spotify:track:b", isPlaying: true));

        _queueService.Setup(q => q.GetQueue(_party.Id)).Returns([]);

        await RunCycles(2);

        _queueService.Verify(q => q.Dequeue(It.IsAny<string>()), Times.Never);
    }

    // --- Grace period ---

    [Test]
    public async Task Poll_ForeignTrack_WithinGracePeriod_DoesNotAdvance()
    {
        _monitor.NotifyTrackStarted(_party.Id, "spotify:track:a");
        // Do NOT advance time — still within 5s grace

        _playerService.Setup(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:old", isPlaying: true));

        _queueService.Setup(q => q.GetQueue(_party.Id)).Returns([MakeQueueItemDto()]);

        await RunCycles(1);

        _queueService.Verify(q => q.Dequeue(It.IsAny<string>()), Times.Never);
        _hub.PartyClient.Verify(c => c.PlaybackStateUpdated(It.IsAny<PlaybackStateDto>()), Times.Never);
    }

    [Test]
    public async Task Poll_GracePeriod_CorrectTrackArrives_BroadcastsNormally()
    {
        _monitor.NotifyTrackStarted(_party.Id, "spotify:track:a");

        _playerService.Setup(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true));

        await RunCycles(1);

        _hub.PartyClient.Verify(c => c.PlaybackStateUpdated(It.IsAny<PlaybackStateDto>()), Times.AtLeastOnce);
    }

    // --- Idle mode ---

    [Test]
    public async Task Poll_QueueEmpty_AfterAdvance_EntersIdleMode()
    {
        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true))
            .ReturnsAsync((PlaybackStateDto?)null);

        _queueService.Setup(q => q.Dequeue(_party.Id)).Returns((QueueItem?)null);

        await RunCycles(2);

        _monitor.GetCachedPlaybackState(_party.Id).Should().BeNull();
    }

    [Test]
    public async Task Poll_IdleWatching_QueueGetsItems_SpotifyNotPlaying_Advances()
    {
        var nextItem = TestData.CreateQueueItem("Queued");

        var pollCall = 0;
        _playerService.Setup(p => p.GetPlaybackStateAsync()).ReturnsAsync(() =>
        {
            pollCall++;
            return pollCall switch
            {
                1 => MakeState("spotify:track:a", isPlaying: true),
                2 => null,
                _ => MakeState("spotify:track:a", isPlaying: false)
            };
        });

        var dequeueCall = 0;
        _queueService.Setup(q => q.Dequeue(_party.Id)).Returns(() =>
        {
            dequeueCall++;
            return dequeueCall == 1 ? null : nextItem;
        });

        _queueService.Setup(q => q.GetQueue(_party.Id)).Returns(
            [MakeQueueItemDto(nextItem.TrackUri)]);

        _playerService.Setup(p => p.PlayTrackAsync(nextItem.TrackUri, It.IsAny<string?>())).ReturnsAsync(true);

        await RunCycles(3);

        _playerService.Verify(p => p.PlayTrackAsync(nextItem.TrackUri, It.IsAny<string?>()), Times.AtLeastOnce);
    }

    // --- Device fallback ---

    [Test]
    public async Task Advance_FirstPlaySucceeds_DoesNotQueryDevices()
    {
        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true))
            .ReturnsAsync((PlaybackStateDto?)null);

        var next = TestData.CreateQueueItem();
        _queueService.Setup(q => q.Dequeue(_party.Id)).Returns(next);
        _playerService.Setup(p => p.PlayTrackAsync(next.TrackUri, It.IsAny<string?>())).ReturnsAsync(true);

        await RunCycles(2);

        _playerService.Verify(p => p.GetDevicesAsync(), Times.Never);
    }

    [Test]
    public async Task Advance_FirstPlayFails_FallsBackToActiveDevice()
    {
        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true))
            .ReturnsAsync((PlaybackStateDto?)null);

        var next = TestData.CreateQueueItem();
        _queueService.Setup(q => q.Dequeue(_party.Id)).Returns(next);

        _playerService.SetupSequence(p => p.PlayTrackAsync(next.TrackUri, It.IsAny<string?>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        _playerService.Setup(p => p.GetDevicesAsync()).ReturnsAsync([
            new SpotifyDeviceDto { Id = "dev-active", Name = "Active", Type = "Speaker", IsActive = true }
        ]);

        await RunCycles(2);

        _playerService.Verify(p => p.PlayTrackAsync(next.TrackUri, "dev-active"), Times.Once);
    }

    [Test]
    public async Task Advance_FirstPlayFails_NoActiveDevice_FallsBackToFirst()
    {
        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true))
            .ReturnsAsync((PlaybackStateDto?)null);

        var next = TestData.CreateQueueItem();
        _queueService.Setup(q => q.Dequeue(_party.Id)).Returns(next);

        _playerService.SetupSequence(p => p.PlayTrackAsync(next.TrackUri, It.IsAny<string?>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        _playerService.Setup(p => p.GetDevicesAsync()).ReturnsAsync([
            new SpotifyDeviceDto { Id = "dev-first", Name = "First", Type = "Speaker", IsActive = false },
            new SpotifyDeviceDto { Id = "dev-second", Name = "Second", Type = "Speaker", IsActive = false }
        ]);

        await RunCycles(2);

        _playerService.Verify(p => p.PlayTrackAsync(next.TrackUri, "dev-first"), Times.Once);
    }

    [Test]
    public async Task Advance_FirstPlayFails_NoDevices_DoesNotBroadcastNowPlaying()
    {
        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true))
            .ReturnsAsync((PlaybackStateDto?)null);

        var next = TestData.CreateQueueItem();
        _queueService.Setup(q => q.Dequeue(_party.Id)).Returns(next);

        _playerService.Setup(p => p.PlayTrackAsync(next.TrackUri, It.IsAny<string?>())).ReturnsAsync(false);
        _playerService.Setup(p => p.GetDevicesAsync()).ReturnsAsync([]);

        await RunCycles(2);

        _hub.PartyClient.Verify(c => c.NowPlayingChanged(It.Is<PlaybackStateDto>(
            s => s.TrackUri == next.TrackUri)), Times.Never);
        _hub.PartyClient.Verify(c => c.QueueUpdated(It.IsAny<List<QueueItemDto>>()), Times.AtLeastOnce);
    }

    // --- Advance success broadcasts ---

    [Test]
    public async Task Advance_Success_BroadcastsNowPlayingAndQueueUpdated()
    {
        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true))
            .ReturnsAsync((PlaybackStateDto?)null);

        var next = TestData.CreateQueueItem("Next Song");
        _queueService.Setup(q => q.Dequeue(_party.Id)).Returns(next);
        _playerService.Setup(p => p.PlayTrackAsync(next.TrackUri, It.IsAny<string?>())).ReturnsAsync(true);

        await RunCycles(2);

        _hub.PartyClient.Verify(c => c.NowPlayingChanged(It.Is<PlaybackStateDto>(
            s => s.TrackUri == next.TrackUri && s.TrackName == "Next Song")), Times.AtLeastOnce);
        _hub.PartyClient.Verify(c => c.QueueUpdated(It.IsAny<List<QueueItemDto>>()), Times.AtLeastOnce);
    }

    // --- Device ID caching ---

    [Test]
    public async Task Poll_PlaybackState_CachesDeviceId()
    {
        _playerService.SetupSequence(p => p.GetPlaybackStateAsync())
            .ReturnsAsync(MakeState("spotify:track:a", isPlaying: true, deviceId: "dev-1"))
            .ReturnsAsync((PlaybackStateDto?)null);

        var next = TestData.CreateQueueItem();
        _queueService.Setup(q => q.Dequeue(_party.Id)).Returns(next);
        _playerService.Setup(p => p.PlayTrackAsync(next.TrackUri, "dev-1")).ReturnsAsync(true);

        await RunCycles(2);

        _playerService.Verify(p => p.PlayTrackAsync(next.TrackUri, "dev-1"), Times.AtLeastOnce);
    }
}
