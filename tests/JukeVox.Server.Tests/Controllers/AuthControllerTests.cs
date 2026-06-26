using FluentAssertions;
using JukeVox.Server.Controllers;
using JukeVox.Server.Models;
using JukeVox.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace JukeVox.Server.Tests.Controllers;

[TestFixture]
public class AuthControllerTests
{
    private const string FrontendUrl = "https://app.test";
    private const string OAuthStateCookie = "JukeVox.OAuthState";
    private const string State = "test1234:nonce";

    private Mock<ISpotifyAuthService> _authService = null!;
    private Mock<IPartyService> _partyService = null!;
    private AuthController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _authService = new Mock<ISpotifyAuthService>();
        _partyService = new Mock<IPartyService>();

        var config = new Mock<IConfiguration>();
        config.Setup(c => c["FrontendUrl"]).Returns(FrontendUrl);

        _controller = new AuthController(
            _authService.Object,
            _partyService.Object,
            config.Object,
            new Mock<ILogger<AuthController>>().Object);
    }

    private void UseContext(string? storedState)
    {
        var context = new DefaultHttpContext();
        if (storedState != null)
        {
            context.Request.Headers.Cookie = $"{OAuthStateCookie}={storedState}";
        }
        _controller.ControllerContext.HttpContext = context;
    }

    [Test]
    public async Task Callback_SpotifyReturnsError_RedirectsToHostWithError()
    {
        UseContext(storedState: null);

        var result = await _controller.Callback(code: null, state: State, error: "server_error");

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be($"{FrontendUrl}/host?spotify_error=server_error");
        _authService.Verify(a => a.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Callback_MissingCode_RedirectsWithMissingCode()
    {
        UseContext(storedState: null);

        var result = await _controller.Callback(code: null, state: State, error: null);

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be($"{FrontendUrl}/host?spotify_error=missing_code");
        _authService.Verify(a => a.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Callback_StateMismatch_RedirectsWithInvalidState()
    {
        UseContext(storedState: null); // no stored cookie → mismatch

        var result = await _controller.Callback(code: "auth-code", state: State, error: null);

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be($"{FrontendUrl}/host?spotify_error=invalid_state");
        _authService.Verify(a => a.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Callback_StateWithoutDelimiter_RedirectsWithInvalidState()
    {
        UseContext(storedState: "nocolon");

        var result = await _controller.Callback(code: "auth-code", state: "nocolon", error: null);

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be($"{FrontendUrl}/host?spotify_error=invalid_state");
        _authService.Verify(a => a.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Callback_ExchangeFails_RedirectsWithExchangeFailed()
    {
        UseContext(storedState: State);
        _authService.Setup(a => a.ExchangeCodeAsync("auth-code", "test1234"))
            .ReturnsAsync((SpotifyTokens?)null);

        var result = await _controller.Callback(code: "auth-code", state: State, error: null);

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be($"{FrontendUrl}/host?spotify_error=exchange_failed");
        _authService.Verify(a => a.ExchangeCodeAsync("auth-code", "test1234"), Times.Once);
    }

    [Test]
    public async Task Callback_ValidCodeAndState_ExchangesAndRedirectsToHost()
    {
        UseContext(storedState: State);
        _authService.Setup(a => a.ExchangeCodeAsync("auth-code", "test1234"))
            .ReturnsAsync(new SpotifyTokens
            {
                AccessToken = "access",
                RefreshToken = "refresh",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        var result = await _controller.Callback(code: "auth-code", state: State, error: null);

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be($"{FrontendUrl}/host");
        _authService.Verify(a => a.ExchangeCodeAsync("auth-code", "test1234"), Times.Once);
    }
}
