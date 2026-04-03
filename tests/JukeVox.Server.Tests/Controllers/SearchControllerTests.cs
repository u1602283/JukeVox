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
public class SearchControllerTests
{
    [SetUp]
    public void SetUp()
    {
        _searchService = new Mock<ISpotifySearchService>();
        _partyService = new Mock<IPartyService>();
        _controller = new SearchController(_searchService.Object, _partyService.Object);
    }

    private const string PartyId = "test1234";
    private Mock<ISpotifySearchService> _searchService = null!;
    private Mock<IPartyService> _partyService = null!;
    private SearchController _controller = null!;

    [Test]
    public async Task Search_AsHost_ReturnsResults()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        _partyService.Setup(p => p.GetPartyIdForSession("host-session")).Returns(PartyId);
        _partyService.Setup(p => p.IsHost(PartyId, "host-session")).Returns(true);
        var results = new List<SearchResultDto>
        {
            new()
            {
                TrackUri = "uri", TrackName = "Song", ArtistName = "Artist", AlbumName = "Album", DurationMs = 200000
            }
        };
        _searchService.Setup(s => s.SearchAsync("test", 20)).ReturnsAsync(results);

        var result = await _controller.Search("test");

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task Search_AsParticipant_ReturnsResults()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext();
        _partyService.Setup(p => p.GetPartyIdForSession("guest-1")).Returns(PartyId);
        _partyService.Setup(p => p.IsHost(PartyId, "guest-1")).Returns(false);
        _partyService.Setup(p => p.IsParticipant(PartyId, "guest-1")).Returns(true);
        _searchService.Setup(s => s.SearchAsync("test", 20)).ReturnsAsync(new List<SearchResultDto>());

        var result = await _controller.Search("test");

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task Search_NoParty_ReturnsUnauthorized()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext("stranger");
        _partyService.Setup(p => p.GetPartyIdForSession("stranger")).Returns((string?)null);

        var result = await _controller.Search("test");

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task Search_NotParticipant_ReturnsUnauthorized()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateGuestContext("stranger");
        _partyService.Setup(p => p.GetPartyIdForSession("stranger")).Returns(PartyId);
        _partyService.Setup(p => p.IsHost(PartyId, "stranger")).Returns(false);
        _partyService.Setup(p => p.IsParticipant(PartyId, "stranger")).Returns(false);

        var result = await _controller.Search("test");

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task Search_EmptyQuery_ReturnsEmptyArray()
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        _partyService.Setup(p => p.GetPartyIdForSession("host-session")).Returns(PartyId);
        _partyService.Setup(p => p.IsHost(PartyId, "host-session")).Returns(true);

        var result = await _controller.Search("  ");

        result.Should().BeOfType<OkObjectResult>();
    }

    [TestCase(0, 1)]
    [TestCase(100, 50)]
    [TestCase(25, 25)]
    public async Task Search_ClampsLimit(int requested, int expected)
    {
        _controller.ControllerContext.HttpContext = TestHttpContext.CreateHostContext();
        _partyService.Setup(p => p.GetPartyIdForSession("host-session")).Returns(PartyId);
        _partyService.Setup(p => p.IsHost(PartyId, "host-session")).Returns(true);
        _searchService.Setup(s => s.SearchAsync("q", expected)).ReturnsAsync(new List<SearchResultDto>());

        await _controller.Search("q", requested);

        _searchService.Verify(s => s.SearchAsync("q", expected), Times.Once);
    }
}
