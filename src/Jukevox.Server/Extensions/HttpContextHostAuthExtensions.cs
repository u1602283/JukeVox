using Microsoft.AspNetCore.DataProtection;

namespace JukeVox.Server.Extensions;

public static class HttpContextHostAuthExtensions
{
    private const string CookieName = "JukeVox.HostAuth";
    private const string Purpose = "JukeVox.HostAuth";

    public static string? GetAuthenticatedHostId(this HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var cookie) ||
            string.IsNullOrEmpty(cookie))
            return null;

        var protector = context.RequestServices
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(Purpose)
            .ToTimeLimitedDataProtector();

        try
        {
            var value = protector.Unprotect(cookie);
            // Legacy cookies contain just "host" — treat as unauthenticated
            if (value == "host")
                return null;
            return value;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsHostAuthenticated(this HttpContext context)
    {
        return context.GetAuthenticatedHostId() != null;
    }

    public static void SetHostAuthCookie(this HttpContext context, string hostId)
    {
        var protector = context.RequestServices
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(Purpose)
            .ToTimeLimitedDataProtector();

        var token = protector.Protect(hostId, TimeSpan.FromHours(24));

        context.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            MaxAge = TimeSpan.FromHours(24)
        });
    }

    public static void ClearHostAuthCookie(this HttpContext context)
    {
        context.Response.Cookies.Delete(CookieName);
    }
}
