using FluentAssertions;
using JukeVox.Server.Middleware;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace JukeVox.Server.Tests.Middleware;

[TestFixture]
public class PartySessionMiddlewareTests
{
    private static readonly IDataProtectionProvider DataProtectionProvider = new EphemeralDataProtectionProvider();

    private PartySessionMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new PartySessionMiddleware(next, DataProtectionProvider);
    }

    private static DefaultHttpContext CreateContext(string? cookieValue = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(DataProtectionProvider);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (cookieValue != null)
        {
            context.Request.Headers.Cookie = $"JukeVox.SessionId={cookieValue}";
        }

        return context;
    }

    [Test]
    public async Task NoCookie_CreatesNewSessionAndSetsCookie()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        var sessionId = context.Items["SessionId"] as string;
        sessionId.Should().NotBeNullOrEmpty().And.HaveLength(32);
        context.Response.Headers.SetCookie.ToString().Should().Contain("JukeVox.SessionId=");
    }

    [Test]
    public async Task ValidCookie_PreservesExistingSession()
    {
        // First request to get a valid cookie
        var middleware = CreateMiddleware();
        var context1 = CreateContext();
        await middleware.InvokeAsync(context1);
        var sessionId1 = context1.Items["SessionId"] as string;
        var setCookie = context1.Response.Headers.SetCookie.ToString();
        var cookieValue = setCookie.Split('=', 2)[1].Split(';')[0];

        // Second request with the cookie
        var context2 = CreateContext(cookieValue);
        await middleware.InvokeAsync(context2);
        var sessionId2 = context2.Items["SessionId"] as string;

        sessionId2.Should().Be(sessionId1);
    }

    [Test]
    public async Task TamperedCookie_CreatesNewSession()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("garbage-tampered-cookie-value");

        await middleware.InvokeAsync(context);

        var sessionId = context.Items["SessionId"] as string;
        sessionId.Should().NotBeNullOrEmpty().And.HaveLength(32);
    }

    [Test]
    public async Task EmptyCookie_CreatesNewSession()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("");

        await middleware.InvokeAsync(context);

        (context.Items["SessionId"] as string).Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task SessionId_SetBeforeNextMiddleware()
    {
        string? capturedSessionId = null;
        var middleware = CreateMiddleware(ctx =>
        {
            capturedSessionId = ctx.Items["SessionId"] as string;
            return Task.CompletedTask;
        });
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        capturedSessionId.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Cookie_HasCorrectOptions()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        var setCookie = context.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("httponly");
        setCookie.Should().Contain("samesite=lax");
        setCookie.Should().Contain("secure");
    }
}
