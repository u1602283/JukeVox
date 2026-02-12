using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using JukeVox.Server.Controllers;
using JukeVox.Server.Models;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Services;
using JukeVox.Server.Tests.Helpers;

namespace JukeVox.Server.Tests.Controllers;

[TestFixture]
public class PlaybackControllerTests
{
    private Mock<ISpotifyPlayerService> _playerService = null!;
    private Mock<IPartyService> _partyService = null!;
    private Mock<IQueueService> _queueService = null!;
    private Mock<IPlaybackMonitorService> _monitorService = null!;
    private MockHubContext _hub = null!;
    private PlaybackController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _playerService = new Mock<ISpotifyPlayerService>();
        _partyService = new Mock<IPartyService>();
        _queueService = new Mock<IQueueService>();
        _monitorService = new Mock<IPlaybackMonitorService>();
        _hub = new MockHubContext();

        _controller = new PlaybackController(
            _playerService.Object,
            _partyService.Object,
            _queueService.Object,
            _monitorService.Object,
            _hub.HubContext.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _playerService.VerifyAll();
        _playerService.VerifyNoOtherCalls();
        _partyService.VerifyAll();
        _partyService.VerifyNoOtherCalls();
        _queueService.VerifyAll();
        _queueService.VerifyNoOtherCalls();
        _monitorService.VerifyAll();
        _monitorService.VerifyNoOtherCalls();
        _hub.PartyClient.VerifyAll();
        _hub.PartyClient.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Pause_NotHost_ReturnsForbid()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext();

        var result = await _controller.Pause();

        result.Should().BeOfType<ForbidResult>();
    }

    [Test]
    public async Task Pause_Host_Success_ReturnsOk()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        _playerService.Setup(p => p.PauseAsync()).ReturnsAsync(true).Verifiable(Times.Once);

        var result = await _controller.Pause();

        result.Should().BeOfType<OkResult>();
    }

    [Test]
    public async Task Pause_Host_SpotifyFails_Returns502()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        _playerService.Setup(p => p.PauseAsync()).ReturnsAsync(false).Verifiable(Times.Once);

        var result = await _controller.Pause();

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(502);
    }

    [Test]
    public async Task Resume_Host_Success_ReturnsOk()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        _playerService.Setup(p => p.ResumeAsync()).ReturnsAsync(true).Verifiable(Times.Once);

        var result = await _controller.Resume();

        result.Should().BeOfType<OkResult>();
    }

    [Test]
    public async Task Previous_ProgressOver5s_SeeksToZero()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        _playerService.Setup(p => p.SeekAsync(0)).ReturnsAsync(true).Verifiable(Times.Once);

        var result = await _controller.Previous(progressMs: 6000);

        result.Should().BeOfType<OkResult>();
        // VerifyNoOtherCalls in TearDown ensures SkipToPrevious was never called
    }

    [Test]
    public async Task Previous_Under5s_WithHistory_PlaysPreviousTrack()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        var prevTrack = TestData.CreateQueueItem("Previous Song");
        var party = TestData.CreateParty();
        _queueService.Setup(q => q.SkipToPrevious()).Returns(prevTrack).Verifiable(Times.Once);
        _partyService.Setup(p => p.GetCurrentParty()).Returns(party).Verifiable(Times.Once);
        _playerService.Setup(p => p.PlayTrackAsync(prevTrack.TrackUri, null)).ReturnsAsync(true).Verifiable(Times.Once);
        _monitorService.Setup(m => m.NotifyTrackStarted(prevTrack.TrackUri)).Verifiable(Times.Once);
        _monitorService.Setup(m => m.GetCachedPlaybackState()).Returns((PlaybackStateDto?)null).Verifiable(Times.Once);
        _queueService.Setup(q => q.GetQueue()).Returns([]).Verifiable(Times.Once);
        _hub.PartyClient.Setup(c => c.NowPlayingChanged(It.IsAny<PlaybackStateDto>())).Returns(Task.CompletedTask).Verifiable(Times.Once);
        _hub.PartyClient.Setup(c => c.QueueUpdated(It.IsAny<List<QueueItemDto>>())).Returns(Task.CompletedTask).Verifiable(Times.Once);

        var result = await _controller.Previous(progressMs: 3000);

        result.Should().BeOfType<OkResult>();
    }

    [Test]
    public async Task Previous_Under5s_NoHistory_SeeksToZero()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        _queueService.Setup(q => q.SkipToPrevious()).Returns((QueueItem?)null).Verifiable(Times.Once);
        _playerService.Setup(p => p.SeekAsync(0)).ReturnsAsync(true).Verifiable(Times.Once);

        var result = await _controller.Previous(progressMs: 3000);

        result.Should().BeOfType<OkResult>();
    }

    [Test]
    public async Task Skip_WithQueueItem_PlaysNextTrack()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        var nextTrack = TestData.CreateQueueItem("Next Song");
        var party = TestData.CreateParty();
        _partyService.Setup(p => p.GetCurrentParty()).Returns(party).Verifiable(Times.Once);
        _queueService.Setup(q => q.Dequeue()).Returns(nextTrack).Verifiable(Times.Once);
        _playerService.Setup(p => p.PlayTrackAsync(nextTrack.TrackUri, null)).ReturnsAsync(true).Verifiable(Times.Once);
        _monitorService.Setup(m => m.NotifyTrackStarted(nextTrack.TrackUri)).Verifiable(Times.Once);
        _monitorService.Setup(m => m.GetCachedPlaybackState()).Returns((PlaybackStateDto?)null).Verifiable(Times.Once);
        _queueService.Setup(q => q.GetQueue()).Returns([]).Verifiable(Times.Once);
        _hub.PartyClient.Setup(c => c.NowPlayingChanged(It.IsAny<PlaybackStateDto>())).Returns(Task.CompletedTask).Verifiable(Times.Once);
        _hub.PartyClient.Setup(c => c.QueueUpdated(It.IsAny<List<QueueItemDto>>())).Returns(Task.CompletedTask).Verifiable(Times.Once);

        var result = await _controller.Skip();

        result.Should().BeOfType<OkResult>();
    }

    [Test]
    public async Task Skip_EmptyQueue_CallsSpotifySkipNext()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        var party = TestData.CreateParty();
        _partyService.Setup(p => p.GetCurrentParty()).Returns(party).Verifiable(Times.Once);
        _queueService.Setup(q => q.Dequeue()).Returns((QueueItem?)null).Verifiable(Times.Once);
        _playerService.Setup(p => p.SkipNextAsync()).ReturnsAsync(true).Verifiable(Times.Once);

        var result = await _controller.Skip();

        result.Should().BeOfType<OkResult>();
    }

    [Test]
    public async Task Skip_NotHost_ReturnsForbid()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext();

        var result = await _controller.Skip();

        result.Should().BeOfType<ForbidResult>();
    }
}
