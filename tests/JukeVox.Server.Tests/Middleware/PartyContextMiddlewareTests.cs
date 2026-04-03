using FluentAssertions;
using JukeVox.Server.Middleware;
using JukeVox.Server.Services;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;

namespace JukeVox.Server.Tests.Middleware;

[TestFixture]
public class PartyContextMiddlewareTests
{
    [Test]
    public async Task WithSessionId_SetsPartyContextFromService()
    {
        var partyService = new Mock<IPartyService>();
        partyService.Setup(p => p.GetPartyIdForSession("session-1")).Returns("party-abc");
        var accessor = new PartyContextAccessor();
        var context = new DefaultHttpContext();
        context.Items["SessionId"] = "session-1";

        var middleware = new PartyContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, partyService.Object, accessor);

        accessor.PartyId.Should().Be("party-abc");
    }

    [Test]
    public async Task NoSessionId_LeavesAccessorNull()
    {
        var partyService = new Mock<IPartyService>();
        var accessor = new PartyContextAccessor();
        var context = new DefaultHttpContext();

        var middleware = new PartyContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, partyService.Object, accessor);

        accessor.PartyId.Should().BeNull();
    }

    [Test]
    public async Task SessionNotMapped_LeavesAccessorNull()
    {
        var partyService = new Mock<IPartyService>();
        partyService.Setup(p => p.GetPartyIdForSession("orphan")).Returns((string?)null);
        var accessor = new PartyContextAccessor();
        var context = new DefaultHttpContext();
        context.Items["SessionId"] = "orphan";

        var middleware = new PartyContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, partyService.Object, accessor);

        accessor.PartyId.Should().BeNull();
    }
}
