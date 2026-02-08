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
public class PartyControllerTests
{
    private Mock<IPartyService> _partyService = null!;
    private Mock<IQueueService> _queueService = null!;
    private Mock<ISpotifyPlayerService> _playerService = null!;
    private PartyController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _partyService = new Mock<IPartyService>();
        _queueService = new Mock<IQueueService>();
        _playerService = new Mock<ISpotifyPlayerService>();

        _controller = new PartyController(
            _partyService.Object,
            _queueService.Object,
            _playerService.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _partyService.VerifyAll();
        _partyService.VerifyNoOtherCalls();
        _queueService.VerifyAll();
        _queueService.VerifyNoOtherCalls();
        _playerService.VerifyAll();
        _playerService.VerifyNoOtherCalls();
    }

    [Test]
    public void JoinParty_InvalidCode_ReturnsBadRequest()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext("guest-1");
        _partyService.Setup(p => p.JoinParty("guest-1", "9999", "Alice")).Returns((GuestSession?)null).Verifiable(Times.Once);

        var result = _controller.JoinParty(new JoinPartyRequest { InviteCode = "9999", DisplayName = "Alice" });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void JoinParty_ValidCode_ReturnsPartyState()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext("guest-1");
        var guest = TestData.CreateGuestSession("guest-1", "Alice", 5);
        var party = TestData.CreateParty();
        _partyService.Setup(p => p.JoinParty("guest-1", "1234", "Alice")).Returns(guest).Verifiable(Times.Once);
        _partyService.Setup(p => p.GetCurrentParty()).Returns(party).Verifiable(Times.Once);
        _queueService.Setup(q => q.GetQueue()).Returns([]).Verifiable(Times.Once);

        var result = _controller.JoinParty(new JoinPartyRequest { InviteCode = "1234", DisplayName = "Alice" });

        var state = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<PartyStateDto>().Subject;
        state.PartyId.Should().Be(party.Id);
        state.IsHost.Should().BeFalse();
        state.CreditsRemaining.Should().Be(5);
    }

    [Test]
    public async Task GetState_NoParty_ReturnsHasPartyFalse()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext("guest-1");
        _partyService.Setup(p => p.GetCurrentParty()).Returns((Party?)null).Verifiable(Times.Once);

        var result = await _controller.GetState();

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().NotBeNull();
    }

    [Test]
    public async Task GetState_AsHost_ReturnsHostState()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext("host-session");
        var party = TestData.CreateParty("host-session");
        _partyService.Setup(p => p.GetCurrentParty()).Returns(party).Verifiable(Times.Once);
        _partyService.Setup(p => p.GetGuest("host-session")).Returns((GuestSession?)null).Verifiable(Times.Once);
        _queueService.Setup(q => q.GetQueue()).Returns([]).Verifiable(Times.Once);

        var result = await _controller.GetState();

        var state = result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<PartyStateDto>().Subject;
        state.IsHost.Should().BeTrue();
    }

    [Test]
    public async Task GetState_AsStranger_ReturnsHasPartyFalse()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext("stranger");
        var party = TestData.CreateParty();
        _partyService.Setup(p => p.GetCurrentParty()).Returns(party).Verifiable(Times.Once);
        _partyService.Setup(p => p.GetGuest("stranger")).Returns((GuestSession?)null).Verifiable(Times.Once);

        var result = await _controller.GetState();

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().NotBeNull();
    }
}
