using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using JukeVox.Server.Middleware;
using JukeVox.Server.Services;

namespace JukeVox.Server.Tests.Middleware;

[TestFixture]
public class HostActivityMiddlewareTests
{
    private static readonly IDataProtectionProvider DataProtectionProvider = new EphemeralDataProtectionProvider();

    private static DefaultHttpContext CreateHostContext(string partyId)
    {
        var services = new ServiceCollection();
        services.AddSingleton(DataProtectionProvider);
        // No HostCredentialService registered — GetAuthenticatedHostId skips credential check
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        var protector = DataProtectionProvider
            .CreateProtector("JukeVox.HostAuth")
            .ToTimeLimitedDataProtector();
        var token = protector.Protect("test-host", TimeSpan.FromHours(24));
        context.Request.Headers.Cookie = $"JukeVox.HostAuth={token}";

        return context;
    }

    [Test]
    public async Task AuthenticatedHost_WithParty_RecordsActivity()
    {
        var monitorService = new Mock<IPlaybackMonitorService>();
        var accessor = new PartyContextAccessor { PartyId = "party-1" };
        bool nextCalled = false;
        var middleware = new HostActivityMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateHostContext("party-1");

        await middleware.InvokeAsync(context, accessor, monitorService.Object);

        nextCalled.Should().BeTrue();
        monitorService.Verify(m => m.RecordHostActivity("party-1"), Times.Once);
    }

    [Test]
    public async Task Guest_DoesNotRecordActivity()
    {
        var monitorService = new Mock<IPlaybackMonitorService>();
        var accessor = new PartyContextAccessor { PartyId = "party-1" };
        var middleware = new HostActivityMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(DataProtectionProvider)
                .BuildServiceProvider()
        };

        await middleware.InvokeAsync(context, accessor, monitorService.Object);

        monitorService.Verify(m => m.RecordHostActivity(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task AuthenticatedHost_NoParty_DoesNotRecordActivity()
    {
        var monitorService = new Mock<IPlaybackMonitorService>();
        var accessor = new PartyContextAccessor(); // PartyId is null
        var middleware = new HostActivityMiddleware(_ => Task.CompletedTask);
        var context = CreateHostContext("party-1");

        await middleware.InvokeAsync(context, accessor, monitorService.Object);

        monitorService.Verify(m => m.RecordHostActivity(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Middleware_CallsNextBeforeRecording()
    {
        var callOrder = new List<string>();
        var monitorService = new Mock<IPlaybackMonitorService>();
        monitorService.Setup(m => m.RecordHostActivity(It.IsAny<string>()))
            .Callback(() => callOrder.Add("record"));
        var accessor = new PartyContextAccessor { PartyId = "party-1" };
        var middleware = new HostActivityMiddleware(ctx =>
        {
            callOrder.Add("next");
            return Task.CompletedTask;
        });
        var context = CreateHostContext("party-1");

        await middleware.InvokeAsync(context, accessor, monitorService.Object);

        callOrder.Should().Equal("next", "record");
    }
}
