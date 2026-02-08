using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using JukeVox.Server.Configuration;
using JukeVox.Server.Models;
using JukeVox.Server.Services;
using JukeVox.Server.Tests.Helpers;

namespace JukeVox.Server.Tests.Services;

[TestFixture]
public class SpotifyAuthServiceTests
{
    private MockHttpHandler _handler = null!;
    private Mock<IPartyService> _partyService = null!;
    private SpotifyAuthService _service = null!;

    private static readonly SpotifyOptions Options = new()
    {
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        RedirectUri = "https://127.0.0.1:5001/api/auth/callback"
    };

    [SetUp]
    public void SetUp()
    {
        _handler = new MockHttpHandler();
        _partyService = new Mock<IPartyService>();
        _service = new SpotifyAuthService(
            Microsoft.Extensions.Options.Options.Create(Options),
            new HttpClient(_handler),
            _partyService.Object,
            NullLogger<SpotifyAuthService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _partyService.VerifyAll();
        _partyService.VerifyNoOtherCalls();
        _handler.Dispose();
    }

    [Test]
    public void GetAuthorizeUrl_ContainsClientIdAndScopes()
    {
        var url = _service.GetAuthorizeUrl("test-state");

        url.Should().Contain("client_id=test-client-id");
        url.Should().Contain("state=test-state");
        url.Should().Contain("response_type=code");
        url.Should().Contain("user-read-playback-state");
        url.Should().Contain("redirect_uri=");
    }

    [Test]
    public async Task ExchangeCodeAsync_Success_ReturnsTokensAndPersists()
    {
        _handler.EnqueueSuccess("""
        {
            "access_token": "new-access-token",
            "token_type": "Bearer",
            "expires_in": 3600,
            "refresh_token": "new-refresh-token",
            "scope": "user-read-playback-state"
        }
        """);

        _partyService.Setup(p => p.SetSpotifyTokens(It.Is<SpotifyTokens>(
            t => t.AccessToken == "new-access-token"))).Verifiable(Times.Once);
        _partyService.Setup(p => p.PersistState()).Verifiable(Times.Once);

        var tokens = await _service.ExchangeCodeAsync("auth-code");

        tokens.Should().NotBeNull();
        tokens!.AccessToken.Should().Be("new-access-token");
        tokens.RefreshToken.Should().Be("new-refresh-token");
        tokens.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Test]
    public async Task ExchangeCodeAsync_NoRefreshToken_DefaultsToEmpty()
    {
        _handler.EnqueueSuccess("""
        {
            "access_token": "access",
            "token_type": "Bearer",
            "expires_in": 3600
        }
        """);

        _partyService.Setup(p => p.SetSpotifyTokens(It.IsAny<SpotifyTokens>())).Verifiable(Times.Once);
        _partyService.Setup(p => p.PersistState()).Verifiable(Times.Once);

        var tokens = await _service.ExchangeCodeAsync("code");

        tokens.Should().NotBeNull();
        tokens!.RefreshToken.Should().BeEmpty();
    }

    [Test]
    public async Task ExchangeCodeAsync_Failure_ReturnsNull()
    {
        _handler.EnqueueError(HttpStatusCode.BadRequest);

        var tokens = await _service.ExchangeCodeAsync("bad-code");

        tokens.Should().BeNull();
        // VerifyNoOtherCalls in TearDown ensures SetSpotifyTokens was never called
    }

    [Test]
    public async Task ExchangeCodeAsync_SendsBasicAuth()
    {
        _handler.EnqueueSuccess("""{"access_token":"a","expires_in":3600}""");

        _partyService.Setup(p => p.SetSpotifyTokens(It.IsAny<SpotifyTokens>())).Verifiable(Times.Once);
        _partyService.Setup(p => p.PersistState()).Verifiable(Times.Once);

        await _service.ExchangeCodeAsync("code");

        var request = _handler.Requests[0];
        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Basic");
        var decoded = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(request.Headers.Authorization.Parameter!));
        decoded.Should().Be("test-client-id:test-client-secret");
    }

    [Test]
    public async Task GetValidAccessTokenAsync_NoTokens_ReturnsNull()
    {
        _partyService.Setup(p => p.GetSpotifyTokens()).Returns((SpotifyTokens?)null).Verifiable(Times.Once);

        var token = await _service.GetValidAccessTokenAsync();

        token.Should().BeNull();
    }

    [Test]
    public async Task GetValidAccessTokenAsync_ValidToken_ReturnsWithoutRefresh()
    {
        var tokens = new SpotifyTokens
        {
            AccessToken = "valid-token",
            RefreshToken = "refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _partyService.Setup(p => p.GetSpotifyTokens()).Returns(tokens).Verifiable(Times.Once);

        var token = await _service.GetValidAccessTokenAsync();

        token.Should().Be("valid-token");
        _handler.Requests.Should().BeEmpty(); // no HTTP calls
    }

    [Test]
    public async Task GetValidAccessTokenAsync_ExpiredToken_RefreshesAndPersists()
    {
        var tokens = new SpotifyTokens
        {
            AccessToken = "old-token",
            RefreshToken = "my-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5) // expired
        };
        _partyService.Setup(p => p.GetSpotifyTokens()).Returns(tokens).Verifiable(Times.Once);

        _handler.EnqueueSuccess("""
        {
            "access_token": "refreshed-token",
            "token_type": "Bearer",
            "expires_in": 3600,
            "refresh_token": "new-refresh-token"
        }
        """);

        _partyService.Setup(p => p.SetSpotifyTokens(It.Is<SpotifyTokens>(
            t => t.AccessToken == "refreshed-token" && t.RefreshToken == "new-refresh-token"))).Verifiable(Times.Once);
        _partyService.Setup(p => p.PersistState()).Verifiable(Times.Once);

        var token = await _service.GetValidAccessTokenAsync();

        token.Should().Be("refreshed-token");
    }

    [Test]
    public async Task GetValidAccessTokenAsync_RefreshFails_ReturnsNull()
    {
        var tokens = new SpotifyTokens
        {
            AccessToken = "old-token",
            RefreshToken = "refresh",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _partyService.Setup(p => p.GetSpotifyTokens()).Returns(tokens).Verifiable(Times.Once);
        _handler.EnqueueError(HttpStatusCode.Unauthorized);

        var token = await _service.GetValidAccessTokenAsync();

        token.Should().BeNull();
    }

    [Test]
    public async Task GetValidAccessTokenAsync_RefreshKeepsOldRefreshTokenIfNotReturned()
    {
        var tokens = new SpotifyTokens
        {
            AccessToken = "old",
            RefreshToken = "original-refresh",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _partyService.Setup(p => p.GetSpotifyTokens()).Returns(tokens).Verifiable(Times.Once);

        _handler.EnqueueSuccess("""
        {
            "access_token": "new-access",
            "token_type": "Bearer",
            "expires_in": 3600
        }
        """);

        _partyService.Setup(p => p.SetSpotifyTokens(It.Is<SpotifyTokens>(
            t => t.RefreshToken == "original-refresh"))).Verifiable(Times.Once);
        _partyService.Setup(p => p.PersistState()).Verifiable(Times.Once);

        await _service.GetValidAccessTokenAsync();
    }
}
