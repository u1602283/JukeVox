using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using JukeVox.Server.Controllers;
using JukeVox.Server.Models;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Services;
using JukeVox.Server.Tests.Helpers;

namespace JukeVox.Server.Tests.Controllers;

[TestFixture]
public class HostPartyControllerTests
{
    private const string PartyId = "test1234";
    private const string HostId = "test-host-id";
    private Mock<IPartyService> _partyService = null!;
    private Mock<IQueueService> _queueService = null!;
    private Mock<ISpotifyPlayerService> _playerService = null!;
    private Mock<ISpotifyPlaylistService> _playlistService = null!;
    private Mock<IPlaybackMonitorService> _monitorService = null!;
    private MockHubContext _hub = null!;
    private ConnectionMapping _connectionMapping = null!;
    private HostCredentialService _credentialService = null!;
    private string _tempDir = null!;
    private HostPartyController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _partyService = new Mock<IPartyService>();
        _queueService = new Mock<IQueueService>();
        _playerService = new Mock<ISpotifyPlayerService>();
        _playlistService = new Mock<ISpotifyPlaylistService>();
        _monitorService = new Mock<IPlaybackMonitorService>();
        _hub = new MockHubContext();
        _connectionMapping = new ConnectionMapping();

        _tempDir = Path.Combine(Path.GetTempPath(), $"jukevox-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_tempDir);
        _credentialService = new HostCredentialService(env.Object, NullLogger<HostCredentialService>.Instance);

        _controller = new HostPartyController(
            _partyService.Object,
            _queueService.Object,
            _playerService.Object,
            _playlistService.Object,
            _monitorService.Object,
            _hub.HubContext.Object,
            _connectionMapping,
            _credentialService);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void SetupActiveParty()
    {
        _partyService.Setup(p => p.GetPartyIdForSession("host-session")).Returns(PartyId);
    }

    [Test]
    public void GetGuests_AsHost_ReturnsGuestList()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext(hostId: HostId);
        SetupActiveParty();
        var guests = new List<GuestSession>
        {
            new() { SessionId = "g1", DisplayName = "Alice", CreditsRemaining = 5 },
            new() { SessionId = "g2", DisplayName = "Bob", CreditsRemaining = 3 }
        };
        _partyService.Setup(p => p.GetAllGuests(PartyId)).Returns(guests);

        var result = _controller.GetGuests();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<List<GuestDto>>().Subject;
        dtos.Should().HaveCount(2);
        dtos[0].DisplayName.Should().Be("Alice");
        dtos[1].DisplayName.Should().Be("Bob");
    }

    [Test]
    public void GetGuests_AsGuest_ReturnsUnauthorized()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext();

        var result = _controller.GetGuests();

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Test]
    public async Task SetGuestCredits_ValidGuest_ReturnsUpdatedGuest()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext(hostId: HostId);
        SetupActiveParty();
        var guest = new GuestSession { SessionId = "g1", DisplayName = "Alice", CreditsRemaining = 10 };
        _partyService.Setup(p => p.SetGuestCredits(PartyId, "g1", 10)).Returns(guest);

        var result = await _controller.SetGuestCredits("g1", new AdjustCreditsRequest { Credits = 10 });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<GuestDto>().Subject;
        dto.CreditsRemaining.Should().Be(10);
    }

    [Test]
    public async Task SetGuestCredits_UnknownGuest_ReturnsNotFound()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext(hostId: HostId);
        SetupActiveParty();
        _partyService.Setup(p => p.SetGuestCredits(PartyId, "nobody", 10)).Returns((GuestSession?)null);

        var result = await _controller.SetGuestCredits("nobody", new AdjustCreditsRequest { Credits = 10 });

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Test]
    public async Task SetGuestCredits_BroadcastsToConnectedGuest()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext(hostId: HostId);
        SetupActiveParty();
        var guest = new GuestSession { SessionId = "g1", DisplayName = "Alice", CreditsRemaining = 7 };
        _partyService.Setup(p => p.SetGuestCredits(PartyId, "g1", 7)).Returns(guest);

        _connectionMapping.Add("g1", "conn-123");
        var mockClient = new Mock<JukeVox.Server.Hubs.IPartyClient>();
        _hub.HubClients.Setup(c => c.Client("conn-123")).Returns(mockClient.Object);
        mockClient.Setup(c => c.CreditsUpdated(7)).Returns(Task.CompletedTask);

        await _controller.SetGuestCredits("g1", new AdjustCreditsRequest { Credits = 7 });

        mockClient.Verify(c => c.CreditsUpdated(7), Times.Once);
    }

    [Test]
    public async Task AdjustAllCredits_ReturnsAllGuests()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext(hostId: HostId);
        SetupActiveParty();
        var guests = new List<GuestSession>
        {
            new() { SessionId = "g1", DisplayName = "Alice", CreditsRemaining = 8 },
            new() { SessionId = "g2", DisplayName = "Bob", CreditsRemaining = 8 }
        };
        _partyService.Setup(p => p.AdjustAllCredits(PartyId, 3)).Returns(guests);

        var result = await _controller.AdjustAllCredits(new BulkAdjustCreditsRequest { Credits = 3 });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<List<GuestDto>>().Subject;
        dtos.Should().HaveCount(2);
    }

    [Test]
    public async Task EndParty_PausesAndBroadcastsAndClears()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext(hostId: HostId);
        SetupActiveParty();
        _playerService.Setup(p => p.PauseAsync()).ReturnsAsync(true);
        _hub.PartyClient.Setup(c => c.PartyEnded()).Returns(Task.CompletedTask);
        _partyService.Setup(p => p.EndParty(PartyId));

        var result = await _controller.EndParty();

        result.Should().BeOfType<OkObjectResult>();
        _playerService.Verify(p => p.PauseAsync(), Times.Once);
        _hub.PartyClient.Verify(c => c.PartyEnded(), Times.Once);
        _partyService.Verify(p => p.EndParty(PartyId), Times.Once);
    }

    [Test]
    public async Task EndParty_NoActiveParty_ReturnsBadRequest()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext(hostId: HostId);
        _partyService.Setup(p => p.GetPartyIdForSession("host-session")).Returns((string?)null);

        var result = await _controller.EndParty();

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
