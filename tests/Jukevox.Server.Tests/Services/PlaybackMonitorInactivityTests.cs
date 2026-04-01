using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using JukeVox.Server.Configuration;
using JukeVox.Server.Hubs;
using JukeVox.Server.Models;
using JukeVox.Server.Services;

namespace JukeVox.Server.Tests.Services;

[TestFixture]
public class PlaybackMonitorInactivityTests
{
    private Mock<IPartyService> _partyService = null!;
    private Mock<IHubContext<PartyHub, IPartyClient>> _hubContext = null!;
    private Mock<IPartyClient> _partyClients = null!;
    private PlaybackMonitorService _monitor = null!;
    private ServiceProvider _serviceProvider = null!;
    private FakeTimeProvider _time = null!;

    [SetUp]
    public void SetUp()
    {
        _partyService = new Mock<IPartyService>();
        _hubContext = new Mock<IHubContext<PartyHub, IPartyClient>>();
        _partyClients = new Mock<IPartyClient>();
        _time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        _hubContext.Setup(h => h.Clients.Group(It.IsAny<string>())).Returns(_partyClients.Object);
        _partyClients.Setup(c => c.PartyWoke()).Returns(Task.CompletedTask);
        _partyClients.Setup(c => c.PartySleeping()).Returns(Task.CompletedTask);
        _partyClients.Setup(c => c.PartyEnded()).Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(_partyService.Object);
        services.AddSingleton(_hubContext.Object);
        services.AddScoped<IPartyContextAccessor, PartyContextAccessor>();
        _serviceProvider = services.BuildServiceProvider();

        var options = Options.Create(new PartyInactivityOptions
        {
            SleepAfterMinutes = 15,
            AutoEndAfterMinutes = 120
        });

        _monitor = new PlaybackMonitorService(
            _serviceProvider,
            NullLogger<PlaybackMonitorService>.Instance,
            options,
            _time);
    }

    [TearDown]
    public void TearDown()
    {
        _monitor.Dispose();
        _serviceProvider.Dispose();
    }

    private async Task RunOnePollCycle()
    {
        using var cts = new CancellationTokenSource();
        await _monitor.StartAsync(cts.Token);
        await Task.Delay(50);
        await cts.CancelAsync();
        await _monitor.StopAsync(CancellationToken.None);
    }

    // --- RecordHostActivity ---

    [Test]
    public void RecordHostActivity_ActiveParty_DoesNotBroadcastWoke()
    {
        var party = new Party { HostSessionId = "host-1", HostId = "h1", Status = PartyStatus.Active };
        _partyService.Setup(p => p.SetPartyStatus(party.Id, PartyStatus.Active, null)).Returns(false);

        _monitor.RecordHostActivity(party.Id);

        _partyService.Verify(p => p.SetPartyStatus(party.Id, PartyStatus.Active, null), Times.Once);
        _partyClients.Verify(c => c.PartyWoke(), Times.Never);
    }

    [Test]
    public void RecordHostActivity_SleepingParty_WakesIt()
    {
        var party = new Party
        {
            HostSessionId = "host-1",
            HostId = "h1",
            Status = PartyStatus.Sleeping,
            SleepingSince = DateTime.UtcNow.AddMinutes(-30)
        };
        _partyService.Setup(p => p.SetPartyStatus(party.Id, PartyStatus.Active, null)).Returns(true);

        _monitor.RecordHostActivity(party.Id);

        _partyService.Verify(p => p.SetPartyStatus(party.Id, PartyStatus.Active, null), Times.Once);
        _partyClients.Verify(c => c.PartyWoke(), Times.Once);
    }

    [Test]
    public void RecordHostActivity_NullParty_DoesNotThrow()
    {
        _partyService.Setup(p => p.GetParty("missing")).Returns((Party?)null);

        var act = () => _monitor.RecordHostActivity("missing");

        act.Should().NotThrow();
    }

    // --- NotifyTrackStarted activity reset ---

    [Test]
    public void NotifyTrackStarted_ResetsLastActivity()
    {
        _monitor.NotifyTrackStarted("party-1", "spotify:track:abc");

        _monitor.GetCachedPlaybackState("party-1").Should().BeNull();
    }

    // --- PollAllPartiesAsync routing ---

    [Test]
    public async Task PollAllParties_SleepingParty_SkipsPlaybackPoll_ChecksAutoEnd()
    {
        var party = new Party
        {
            HostSessionId = "host-1",
            HostId = "h1",
            Status = PartyStatus.Sleeping,
            SleepingSince = _time.GetUtcNow().UtcDateTime.AddMinutes(-10),
            SpotifyTokens = new SpotifyTokens
            {
                AccessToken = "tok", RefreshToken = "ref",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            }
        };
        _partyService.Setup(p => p.GetAllParties()).Returns([party]);

        await RunOnePollCycle();

        _partyService.Verify(p => p.EndParty(party.Id), Times.Never);
        _partyClients.Verify(c => c.PartyEnded(), Times.Never);
    }

    // --- CheckAutoEndAsync ---

    [Test]
    public async Task PollAllParties_SleepingPastThreshold_EndsParty()
    {
        var party = new Party
        {
            HostSessionId = "host-1",
            HostId = "h1",
            Status = PartyStatus.Sleeping,
            SleepingSince = _time.GetUtcNow().UtcDateTime.AddMinutes(-121),
            SpotifyTokens = new SpotifyTokens
            {
                AccessToken = "tok", RefreshToken = "ref",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            }
        };
        _partyService.Setup(p => p.GetAllParties()).Returns([party]);

        await RunOnePollCycle();

        _partyClients.Verify(c => c.PartyEnded(), Times.Once);
        _partyService.Verify(p => p.EndParty(party.Id), Times.Once);
    }

    [Test]
    public async Task PollAllParties_SleepingWithNullSleepingSince_DoesNotEnd()
    {
        var party = new Party
        {
            HostSessionId = "host-1",
            HostId = "h1",
            Status = PartyStatus.Sleeping,
            SleepingSince = null,
            SpotifyTokens = new SpotifyTokens
            {
                AccessToken = "tok", RefreshToken = "ref",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            }
        };
        _partyService.Setup(p => p.GetAllParties()).Returns([party]);

        await RunOnePollCycle();

        _partyService.Verify(p => p.EndParty(It.IsAny<string>()), Times.Never);
    }
}
