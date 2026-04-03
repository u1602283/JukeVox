using FluentAssertions;
using JukeVox.Server.Hubs;
using JukeVox.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Moq;
using NUnit.Framework;

namespace JukeVox.Server.Tests.Hubs;

[TestFixture]
public class PartyHubTests
{
    [SetUp]
    public void SetUp()
    {
        _partyService = new Mock<IPartyService>();
        _connectionMapping = new ConnectionMapping();
        _hub = new PartyHub(_partyService.Object, _connectionMapping);
    }

    [TearDown]
    public void TearDown() => _hub.Dispose();

    private Mock<IPartyService> _partyService = null!;
    private ConnectionMapping _connectionMapping = null!;
    private PartyHub _hub = null!;

    private void SetupHubContext(string connectionId, string? sessionId = null, string? partyId = null)
    {
        var httpContext = new DefaultHttpContext();
        if (sessionId != null)
        {
            httpContext.Items["SessionId"] = sessionId;
        }

        if (partyId != null)
        {
            httpContext.Request.QueryString = new QueryString($"?partyId={partyId}");
        }

        // Set up the HttpContext feature so GetHttpContext() works
        var features = new FeatureCollection();
        var httpContextFeature = new Mock<IHttpContextFeature>();
        httpContextFeature.Setup(f => f.HttpContext).Returns(httpContext);
        features.Set(httpContextFeature.Object);
        var httpConnectionFeature = new Mock<IHttpContextFeature>();

        var hubCallerContext = new Mock<HubCallerContext>();
        hubCallerContext.Setup(c => c.ConnectionId).Returns(connectionId);
        hubCallerContext.Setup(c => c.Features).Returns(features);

        var groups = new Mock<IGroupManager>();
        groups.Setup(g => g.AddToGroupAsync(connectionId, It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);

        _hub.Context = hubCallerContext.Object;
        _hub.Groups = groups.Object;
    }

    // --- JoinPartyGroup ---

    [Test]
    public async Task JoinPartyGroup_ValidParticipant_AddsToGroup()
    {
        SetupHubContext("conn-1", "session-1");
        _partyService.Setup(p => p.IsParticipant("party-1", "session-1")).Returns(true);

        await _hub.JoinPartyGroup("party-1");

        Mock.Get(_hub.Groups)
            .Verify(
                g => g.AddToGroupAsync("conn-1", "party-1", default),
                Times.Once);
    }

    [Test]
    public async Task JoinPartyGroup_NotParticipant_DoesNotAddToGroup()
    {
        SetupHubContext("conn-1", "session-1");
        _partyService.Setup(p => p.IsParticipant("party-1", "session-1")).Returns(false);

        await _hub.JoinPartyGroup("party-1");

        Mock.Get(_hub.Groups)
            .Verify(
                g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default),
                Times.Never);
    }

    [Test]
    public async Task JoinPartyGroup_NoSession_DoesNotAddToGroup()
    {
        SetupHubContext("conn-1"); // no sessionId

        await _hub.JoinPartyGroup("party-1");

        Mock.Get(_hub.Groups)
            .Verify(
                g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default),
                Times.Never);
    }

    // --- OnConnectedAsync ---

    [Test]
    public async Task OnConnected_WithPartyId_Participant_AddsToGroupAndMapsConnection()
    {
        SetupHubContext("conn-1", "session-1", "party-1");
        _partyService.Setup(p => p.IsParticipant("party-1", "session-1")).Returns(true);

        await _hub.OnConnectedAsync();

        Mock.Get(_hub.Groups)
            .Verify(
                g => g.AddToGroupAsync("conn-1", "party-1", default),
                Times.Once);
        _connectionMapping.GetConnectionId("session-1").Should().Be("conn-1");
    }

    [Test]
    public async Task OnConnected_WithPartyId_NotParticipant_Aborts()
    {
        SetupHubContext("conn-1", "session-1", "party-1");
        _partyService.Setup(p => p.IsParticipant("party-1", "session-1")).Returns(false);

        await _hub.OnConnectedAsync();

        Mock.Get(_hub.Context).Verify(c => c.Abort(), Times.Once);
    }

    [Test]
    public async Task OnConnected_NoPartyId_MapsConnectionOnly()
    {
        SetupHubContext("conn-1", "session-1"); // no partyId

        await _hub.OnConnectedAsync();

        Mock.Get(_hub.Groups)
            .Verify(
                g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default),
                Times.Never);
        _connectionMapping.GetConnectionId("session-1").Should().Be("conn-1");
    }

    // --- OnDisconnectedAsync ---

    [Test]
    public async Task OnDisconnected_RemovesConnectionMapping()
    {
        SetupHubContext("conn-1", "session-1");
        _connectionMapping.Add("session-1", "conn-1");

        await _hub.OnDisconnectedAsync(null);

        _connectionMapping.GetConnectionId("session-1").Should().BeNull();
    }
}
