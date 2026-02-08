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
        var party = _partyService.GetCurrentParty();
        if (party != null && party.Id == partyId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, partyId);
        }
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var partyId = httpContext?.Request.Query["partyId"].ToString();

        if (!string.IsNullOrEmpty(partyId))
        {
            var party = _partyService.GetCurrentParty();
            if (party != null && party.Id == partyId)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, partyId);
            }
        }

        // Map session cookie to connection ID for targeted broadcasts
        var sessionId = httpContext?.Request.Cookies[SessionCookieName];
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
