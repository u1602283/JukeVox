using JukeVox.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace JukeVox.Server.Hubs;

public class PartyHub : Hub<IPartyClient>
{
    private readonly ConnectionMapping _connectionMapping;
    private readonly IPartyService _partyService;

    public PartyHub(IPartyService partyService, ConnectionMapping connectionMapping)
    {
        _partyService = partyService;
        _connectionMapping = connectionMapping;
    }

    public async Task JoinPartyGroup(string partyId)
    {
        var sessionId = Context.GetHttpContext()?.Items["SessionId"] as string;
        if (!string.IsNullOrEmpty(sessionId) && _partyService.IsParticipant(partyId, sessionId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, partyId);
        }
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var partyId = httpContext?.Request.Query["partyId"].ToString();
        var sessionId = httpContext?.Items["SessionId"] as string;

        if (!string.IsNullOrEmpty(partyId)
            && !string.IsNullOrEmpty(sessionId)
            && _partyService.IsParticipant(partyId, sessionId))
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
