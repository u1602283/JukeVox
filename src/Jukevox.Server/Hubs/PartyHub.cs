using Microsoft.AspNetCore.SignalR;
using JukeVox.Server.Services;

namespace JukeVox.Server.Hubs;

public class PartyHub : Hub<IPartyClient>
{
    private const string SessionCookieName = "JukeVox.SessionId";
    private readonly IPartyService _partyService;
    private readonly ConnectionMapping _connectionMapping;

    public PartyHub(IPartyService partyService, ConnectionMapping connectionMapping)
    {
        _partyService = partyService;
        _connectionMapping = connectionMapping;
    }

    public async Task JoinPartyGroup(string partyId)
    {
        var httpContext = Context.GetHttpContext();
        var sessionId = httpContext?.Request.Cookies[SessionCookieName];
        if (!string.IsNullOrEmpty(sessionId) && _partyService.IsParticipant(partyId, sessionId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, partyId);
        }
        else
        {
            Context.Abort();
        }
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var partyId = httpContext?.Request.Query["partyId"].ToString();
        var sessionId = httpContext?.Request.Cookies[SessionCookieName];

        if (!string.IsNullOrEmpty(partyId) && !string.IsNullOrEmpty(sessionId) && _partyService.IsParticipant(partyId, sessionId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, partyId);
        }
        else if (!string.IsNullOrEmpty(partyId))
        {
            // Not a participant: disconnect
            Context.Abort();
            return;
        }

        // Map session cookie to connection ID for targeted broadcasts
        if (!string.IsNullOrEmpty(sessionId))
        {
            _connectionMapping.Add(sessionId, Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _connectionMapping.RemoveByConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
