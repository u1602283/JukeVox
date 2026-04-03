using FluentAssertions;
using JukeVox.Server.Controllers;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Services;
using JukeVox.Server.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace JukeVox.Server.Tests.Controllers;

[TestFixture]
public class QueueControllerTests
{
    [SetUp]
    public void SetUp()
    {
        _queueService = new Mock<IQueueService>();
        _partyService = new Mock<IPartyService>();
        _playerService = new Mock<ISpotifyPlayerService>();
        _monitorService = new Mock<IPlaybackMonitorService>();
        _hub = new MockHubContext();

        _controller = new QueueController(
            _queueService.Object,
            _partyService.Object,
            _playerService.Object,
            _monitorService.Object,
            _hub.HubContext.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _queueService.VerifyAll();
        _queueService.VerifyNoOtherCalls();
        _partyService.VerifyAll();
        _partyService.VerifyNoOtherCalls();
        _playerService.VerifyAll();
        _playerService.VerifyNoOtherCalls();
        _monitorService.VerifyAll();
        _monitorService.VerifyNoOtherCalls();
        _hub.PartyClient.VerifyAll();
        _hub.PartyClient.VerifyNoOtherCalls();
    }

    private const string PartyId = "test1234";
    private Mock<IQueueService> _queueService = null!;
    private Mock<IPartyService> _partyService = null!;
    private Mock<ISpotifyPlayerService> _playerService = null!;
    private Mock<IPlaybackMonitorService> _monitorService = null!;
    private MockHubContext _hub = null!;
    private QueueController _controller = null!;

    [Test]
    public void GetQueue_AsHost_ReturnsOk()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        _partyService.Setup(p => p.GetPartyIdForSession("host-session")).Returns(PartyId).Verifiable(Times.Once);
        _partyService.Setup(p => p.IsHost(PartyId, "host-session")).Returns(true).Verifiable(Times.Once);
        var queue = new List<QueueItemDto>
        {
            new()
            {
                Id = "1", TrackUri = "uri", TrackName = "Song", ArtistName = "Artist", AlbumName = "Album",
                AddedByName = "Host"
            }
        };
        _queueService.Setup(q => q.GetQueue(PartyId)).Returns(queue).Verifiable(Times.Once);
        _queueService.Setup(q => q.GetUserVotes(PartyId, "host-session"))
            .Returns(new Dictionary<string, int>())
            .Verifiable(Times.Once);

        var result = _controller.GetQueue();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public void GetQueue_AsGuest_Participant_ReturnsOk()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext();
        _partyService.Setup(p => p.GetPartyIdForSession("guest-1")).Returns(PartyId).Verifiable(Times.Once);
        _partyService.Setup(p => p.IsHost(PartyId, "guest-1")).Returns(false).Verifiable(Times.Once);
        _partyService.Setup(p => p.IsParticipant(PartyId, "guest-1")).Returns(true).Verifiable(Times.Once);
        _queueService.Setup(q => q.GetQueue(PartyId)).Returns([]).Verifiable(Times.Once);
        _queueService.Setup(q => q.GetUserVotes(PartyId, "guest-1"))
            .Returns(new Dictionary<string, int>())
            .Verifiable(Times.Once);

        _controller.GetQueue().Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public void GetQueue_Stranger_ReturnsUnauthorized()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext("stranger");
        _partyService.Setup(p => p.GetPartyIdForSession("stranger")).Returns(PartyId).Verifiable(Times.Once);
        _partyService.Setup(p => p.IsHost(PartyId, "stranger")).Returns(false).Verifiable(Times.Once);
        _partyService.Setup(p => p.IsParticipant(PartyId, "stranger")).Returns(false).Verifiable(Times.Once);

        _controller.GetQueue().Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task AddToQueue_Success_BroadcastsQueue()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        _partyService.Setup(p => p.GetPartyIdForSession("host-session")).Returns(PartyId).Verifiable(Times.Once);
        _partyService.Setup(p => p.IsHost(PartyId, "host-session")).Returns(true).Verifiable(Times.Once);
        var request = TestData.CreateAddToQueueRequest("New Song");
        var addedItem = TestData.CreateQueueItem("New Song");
        var party = TestData.CreateParty();
        party.Id = PartyId;
        var queueDtos = new List<QueueItemDto>();

        _queueService.Setup(q => q.AddToQueue(PartyId, "host-session", request, true))
            .Returns((addedItem, null))
            .Verifiable(Times.Once);
        _partyService.Setup(p => p.GetParty(PartyId)).Returns(party).Verifiable(Times.Once);
        _queueService.Setup(q => q.GetQueue(PartyId)).Returns(queueDtos).Verifiable(Times.Once);
        _hub.PartyClient.Setup(c => c.QueueUpdated(queueDtos)).Returns(Task.CompletedTask).Verifiable(Times.Once);

        var result = await _controller.AddToQueue(request);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task AddToQueue_Failure_ReturnsBadRequest()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext();
        _partyService.Setup(p => p.GetPartyIdForSession("guest-1")).Returns(PartyId).Verifiable(Times.Once);
        _partyService.Setup(p => p.IsHost(PartyId, "guest-1")).Returns(false).Verifiable(Times.Once);
        _partyService.Setup(p => p.IsParticipant(PartyId, "guest-1")).Returns(true).Verifiable(Times.Once);
        var request = TestData.CreateAddToQueueRequest();
        _queueService.Setup(q => q.AddToQueue(PartyId, "guest-1", request, false))
            .Returns((null, "No credits remaining"))
            .Verifiable(Times.Once);

        var result = await _controller.AddToQueue(request);

        result.Should()
            .BeOfType<BadRequestObjectResult>()
            .Which.Value.Should()
            .NotBeNull();
    }

    [Test]
    public async Task RemoveFromQueue_NotHost_ReturnsForbid()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext();

        (await _controller.RemoveFromQueue("item-1")).Should().BeOfType<ForbidResult>();
    }

    [Test]
    public async Task RemoveFromQueue_NotFound_Returns404()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        _partyService.Setup(p => p.GetPartyIdForSession("host-session")).Returns(PartyId).Verifiable(Times.Once);
        _queueService.Setup(q => q.RemoveFromQueue(PartyId, "item-1")).Returns(false).Verifiable(Times.Once);

        (await _controller.RemoveFromQueue("item-1")).Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task Reorder_AsHost_ReturnsOkAndBroadcasts()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        _partyService.Setup(p => p.GetPartyIdForSession("host-session")).Returns(PartyId).Verifiable(Times.Once);
        var orderedIds = new List<string> { "b", "a" };
        var queueDtos = new List<QueueItemDto>();

        _queueService.Setup(q => q.Reorder(PartyId, orderedIds)).Returns(true).Verifiable(Times.Once);
        _queueService.Setup(q => q.GetQueue(PartyId)).Returns(queueDtos).Verifiable(Times.Once);
        _hub.PartyClient.Setup(c => c.QueueUpdated(queueDtos)).Returns(Task.CompletedTask).Verifiable(Times.Once);

        var result = await _controller.Reorder(new ReorderQueueRequest { OrderedIds = orderedIds });

        result.Should().BeOfType<OkObjectResult>();
    }
}
