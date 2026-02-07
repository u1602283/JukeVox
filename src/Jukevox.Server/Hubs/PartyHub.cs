using Microsoft.AspNetCore.SignalR;
using Jukevox.Server.Services;

namespace Jukevox.Server.Hubs;

public class PartyHub : Hub<IPartyClient>
{
    private readonly PartyService _partyService;

    public PartyHub(PartyService partyService)
    {
        _partyService = partyService;
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
        // Auto-join via query string if provided
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

        await base.OnConnectedAsync();
    }
}
