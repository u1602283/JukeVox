using JukeVox.Server.Extensions;
using JukeVox.Server.Services;

namespace JukeVox.Server.Middleware;

public class HostActivityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IPartyContextAccessor accessor,
        IPlaybackMonitorService monitorService)
    {
        await next(context);

        if (context.IsHostAuthenticated() && accessor.PartyId is string partyId)
        {
            monitorService.RecordHostActivity(partyId);
        }
    }
}
