namespace JukeVox.Server.Middleware;

public class PartySessionMiddleware
{
    private const string SessionCookieName = "JukeVox.SessionId";
    private readonly RequestDelegate _next;

    public PartySessionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(SessionCookieName, out var sessionId) ||
            string.IsNullOrEmpty(sessionId))
        {
            sessionId = Guid.NewGuid().ToString("N");
            context.Response.Cookies.Append(SessionCookieName, sessionId, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                MaxAge = TimeSpan.FromHours(24)
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
