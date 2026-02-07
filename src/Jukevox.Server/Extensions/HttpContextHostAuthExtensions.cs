using Microsoft.AspNetCore.DataProtection;

namespace JukeVox.Server.Extensions;

public static class HttpContextHostAuthExtensions
{
    private const string CookieName = "JukeVox.HostAuth";
    private const string Purpose = "JukeVox.HostAuth";

    public static bool IsHostAuthenticated(this HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var cookie) ||
            string.IsNullOrEmpty(cookie))
            return false;

        var protector = context.RequestServices
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(Purpose)
            .ToTimeLimitedDataProtector();

        try
        {
            var value = protector.Unprotect(cookie);
            return value == "host";
        }
        catch
        {
            return false;
        }
    }

    public static void SetHostAuthCookie(this HttpContext context)
    {
        var protector = context.RequestServices
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(Purpose)
            .ToTimeLimitedDataProtector();

        var token = protector.Protect("host", TimeSpan.FromHours(24));

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
