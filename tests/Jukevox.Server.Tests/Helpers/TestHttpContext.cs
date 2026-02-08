using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace JukeVox.Server.Tests.Helpers;

public static class TestHttpContext
{
    private static readonly IDataProtectionProvider DataProtectionProvider =
        new EphemeralDataProtectionProvider();

    public static HttpContext CreateHostContext(string sessionId = "host-session")
    {
        var context = CreateBaseContext(sessionId);

        // Create a real host auth cookie using the same provider that's in DI
        var protector = DataProtectionProvider
            .CreateProtector("JukeVox.HostAuth")
            .ToTimeLimitedDataProtector();
        var token = protector.Protect("host", TimeSpan.FromHours(24));
        context.Request.Headers.Cookie = $"JukeVox.HostAuth={token}";

        return context;
    }

    public static HttpContext CreateGuestContext(string sessionId = "guest-1")
    {
        return CreateBaseContext(sessionId);
    }

    private static HttpContext CreateBaseContext(string sessionId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataProtectionProvider>(DataProtectionProvider);

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        context.Items["SessionId"] = sessionId;

        return context;
    }
}
