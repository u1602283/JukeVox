using JukeVox.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace JukeVox.Server.Tests.Helpers;

public class MockHubContext
{
    public MockHubContext()
    {
        PartyClient = new Mock<IPartyClient>();
        HubClients = new Mock<IHubClients<IPartyClient>>();
        HubContext = new Mock<IHubContext<PartyHub, IPartyClient>>();

        HubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(PartyClient.Object);
        HubContext.Setup(h => h.Clients).Returns(HubClients.Object);
    }

    public Mock<IHubContext<PartyHub, IPartyClient>> HubContext { get; }
    public Mock<IPartyClient> PartyClient { get; }
    public Mock<IHubClients<IPartyClient>> HubClients { get; }
}
