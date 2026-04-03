using Microsoft.AspNetCore.DataProtection;

namespace JukeVox.Server.Middleware;

public class PartySessionMiddleware
{
    private const string SessionCookieName = "JukeVox.SessionId";
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(24);
    private readonly RequestDelegate _next;
    private readonly IDataProtector _protector;

    public PartySessionMiddleware(RequestDelegate next, IDataProtectionProvider dataProtectionProvider)
    {
        _next = next;
        _protector = dataProtectionProvider.CreateProtector("JukeVox.SessionId");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string? sessionId = null;
        var valid = false;

        if (context.Request.Cookies.TryGetValue(SessionCookieName, out var protectedSessionId)
            && !string.IsNullOrEmpty(protectedSessionId))
        {
            try
            {
                // Unprotect and parse the session ID
                var unprotected = _protector.Unprotect(protectedSessionId);
                var parts = unprotected.Split('|');
                if (parts.Length == 2
                    && Guid.TryParse(parts[0], out var guid)
                    && long.TryParse(parts[1], out var ticks))
                {
                    var issued = new DateTimeOffset(ticks, TimeSpan.Zero);
                    if (DateTimeOffset.UtcNow - issued < SessionTtl)
                    {
                        sessionId = guid.ToString("N");
                        valid = true;
                    }
                }
            }
            catch
            {
                // Tampered or expired cookie, fall through to generate new
            }
        }

        if (!valid)
        {
            var guid = Guid.NewGuid();
            sessionId = guid.ToString("N");
            var payload = $"{guid}|{DateTimeOffset.UtcNow.UtcTicks}";
            var protectedPayload = _protector.Protect(payload);
            context.Response.Cookies.Append(SessionCookieName,
                protectedPayload,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = true,
                    MaxAge = SessionTtl
                });
        }

        context.Items["SessionId"] = sessionId;
        await _next(context);
    }
}

public static class HttpContextSessionExtensions
{
    public static string GetSessionId(this HttpContext context)
    {
        return context.Items["SessionId"] as string
               ?? throw new InvalidOperationException("Session middleware not configured");
    }
}
