using JukeVox.Server.Services;

namespace JukeVox.Server.Middleware;

public class PartyContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IPartyService partyService, IPartyContextAccessor accessor)
    {
        if (context.Items["SessionId"] is string sessionId)
        {
            accessor.PartyId = partyService.GetPartyIdForSession(sessionId);
        }

        await next(context);
    }
}
