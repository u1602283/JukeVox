using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using JukeVox.Server.Models;
using JukeVox.Server.Services;

namespace JukeVox.Server.Tests.Services;

[TestFixture]
public class PartyServiceTests
{
    private const string HostId = "test-host-id";
    private string _tempDir = null!;
    private PartyService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"jukevox-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = CreateService(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static PartyService CreateService(string contentRootPath)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(contentRootPath);
        return new PartyService(env.Object, NullLogger<PartyService>.Instance);
    }

    [Test]
    public void GetParty_NoParty_ReturnsNull()
    {
        _service.GetParty("nonexistent").Should().BeNull();
    }

    [Test]
    public void CreateParty_ReturnsPartyWithCorrectValues()
    {
        var party = _service.CreateParty("host-1", HostId, 5);

        party.Should().NotBeNull();
        party.Should().BeEquivalentTo(new
        {
            HostSessionId = "host-1",
            HostId,
            DefaultCredits = 5
        }, options => options.ExcludingMissingMembers());
        party.JoinToken.Should().NotBeNullOrEmpty().And.HaveLength(32);
    }

    [Test]
    public void GetParty_AfterCreate_ReturnsParty()
    {
        var party = _service.CreateParty("host-1", HostId, 5);

        var retrieved = _service.GetParty(party.Id);
        retrieved.Should().NotBeNull();
        retrieved.JoinToken.Should().Be(party.JoinToken);
    }

    [Test]
    public void JoinParty_CorrectCode_ReturnsGuestSession()
    {
        var party = _service.CreateParty("host-1", HostId, 5);

        var (guest, error) = _service.JoinParty("guest-1", party.JoinToken, "Alice");

        error.Should().BeNull();
        guest.Should().NotBeNull();
        guest.Should().BeEquivalentTo(new
        {
            SessionId = "guest-1",
            DisplayName = "Alice",
            CreditsRemaining = 5
        });
    }

    [Test]
    public void JoinParty_WrongCode_ReturnsNull()
    {
        _service.CreateParty("host-1", HostId, 5);

        _service.JoinParty("guest-1", "invalid-token", "Alice").Guest.Should().BeNull();
    }

    [Test]
    public void JoinParty_IdempotentRejoin_ReturnsSameGuest()
    {
        var party = _service.CreateParty("host-1", HostId, 5);
        var (guest1, _) = _service.JoinParty("guest-1", party.JoinToken, "Alice");
        var (guest2, _) = _service.JoinParty("guest-1", party.JoinToken, "Alice");

        guest2.Should().BeSameAs(guest1);
    }

    [Test]
    public void JoinParty_DuplicateName_ReturnsError()
    {
        var party = _service.CreateParty("host-1", HostId, 5);
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var (guest, error) = _service.JoinParty("guest-2", party.JoinToken, "Alice");

        guest.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void JoinParty_DuplicateName_CaseInsensitive()
    {
        var party = _service.CreateParty("host-1", HostId, 5);
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var (guest, error) = _service.JoinParty("guest-2", party.JoinToken, "alice");

        guest.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void IsHost_HostSessionId_ReturnsTrue()
    {
        var party = _service.CreateParty("host-1", HostId, 5);

        _service.IsHost(party.Id, "host-1").Should().BeTrue();
        _service.IsHost(party.Id, "guest-1").Should().BeFalse();
    }

    [Test]
    public void IsParticipant_HostAndGuest_ReturnTrue()
    {
        var party = _service.CreateParty("host-1", HostId, 5);
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        _service.IsParticipant(party.Id, "host-1").Should().BeTrue();
        _service.IsParticipant(party.Id, "guest-1").Should().BeTrue();
        _service.IsParticipant(party.Id, "stranger").Should().BeFalse();
    }

    [Test]
    public void UpdateSettings_ChangesCredits()
    {
        var party = _service.CreateParty("host-1", HostId, 5);

        _service.UpdateSettings(party.Id, 10);

        var retrieved = _service.GetParty(party.Id)!;
        retrieved.DefaultCredits.Should().Be(10);
    }

    [Test]
    public void SpotifyTokens_RoundTrip()
    {
        var party = _service.CreateParty("host-1", HostId, 5);

        var tokens = new SpotifyTokens
        {
            AccessToken = "access",
            RefreshToken = "refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _service.SetSpotifyTokens(party.Id, tokens);

        var retrieved = _service.GetSpotifyTokens(party.Id);
        retrieved.Should().NotBeNull();
        retrieved.AccessToken.Should().Be("access");
        retrieved.RefreshToken.Should().Be("refresh");
    }

    [Test]
    public void ResumeAsHost_UpdatesHostSessionId()
    {
        var party = _service.CreateParty("host-1", HostId, 5);

        var resumed = _service.ResumeAsHost(party.Id, "host-2");

        resumed.Should().NotBeNull();
        resumed.HostSessionId.Should().Be("host-2");
        _service.IsHost(party.Id, "host-2").Should().BeTrue();
        _service.IsHost(party.Id, "host-1").Should().BeFalse();
    }

    [Test]
    public void Persistence_SurvivesNewInstance()
    {
        var party = _service.CreateParty("host-1", HostId, 5);
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var newService = CreateService(_tempDir);

        var retrieved = newService.GetParty(party.Id);
        retrieved.Should().NotBeNull();
        retrieved.JoinToken.Should().Be(party.JoinToken);
        retrieved.Guests.Should().ContainKey("guest-1");
    }

    [Test]
    public void GetAllGuests_ReturnsAllJoinedGuests()
    {
        var party = _service.CreateParty("host-1", HostId, 5);
        _service.JoinParty("guest-1", party.JoinToken, "Alice");
        _service.JoinParty("guest-2", party.JoinToken, "Bob");

        var guests = _service.GetAllGuests(party.Id);

        guests.Should().HaveCount(2);
        guests.Select(g => g.DisplayName).Should().BeEquivalentTo(["Alice", "Bob"]);
    }

    [Test]
    public void GetAllGuests_NoParty_ReturnsEmpty()
    {
        _service.GetAllGuests("nonexistent").Should().BeEmpty();
    }

    [Test]
    public void SetGuestCredits_SetsAbsoluteValue()
    {
        var party = _service.CreateParty("host-1", HostId, 5);
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var guest = _service.SetGuestCredits(party.Id, "guest-1", 10);

        guest.Should().NotBeNull();
        guest.CreditsRemaining.Should().Be(10);
    }

    [Test]
    public void SetGuestCredits_ClampsToZero()
    {
        var party = _service.CreateParty("host-1", HostId, 5);
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var guest = _service.SetGuestCredits(party.Id, "guest-1", -3);

        guest!.CreditsRemaining.Should().Be(0);
    }

    [Test]
    public void SetGuestCredits_UnknownGuest_ReturnsNull()
    {
        var party = _service.CreateParty("host-1", HostId, 5);

        _service.SetGuestCredits(party.Id, "nobody", 10).Should().BeNull();
    }

    [Test]
    public void AdjustAllCredits_AddsDelta()
    {
        var party = _service.CreateParty("host-1", HostId, 5);
        _service.JoinParty("guest-1", party.JoinToken, "Alice");
        _service.JoinParty("guest-2", party.JoinToken, "Bob");

        var guests = _service.AdjustAllCredits(party.Id, 3);

        guests.Should().HaveCount(2);
        guests.Should().AllSatisfy(g => g.CreditsRemaining.Should().Be(8));
    }

    [Test]
    public void AdjustAllCredits_NegativeDelta_ClampsToZero()
    {
        var party = _service.CreateParty("host-1", HostId, 5);
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var guests = _service.AdjustAllCredits(party.Id, -10);

        guests.Should().HaveCount(1);
        guests[0].CreditsRemaining.Should().Be(0);
    }

    [Test]
    public void EndParty_ClearsCurrentParty()
    {
        var party = _service.CreateParty("host-1", HostId, 5);
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        _service.EndParty(party.Id);

        _service.GetParty(party.Id).Should().BeNull();
        _service.GetAllPartySummaries().Should().BeEmpty();
    }

    [Test]
    public void EndParty_DeletesStateFile()
    {
        var party = _service.CreateParty("host-1", HostId, 5);

        _service.EndParty(party.Id);

        var newService = CreateService(_tempDir);
        newService.GetParty(party.Id).Should().BeNull();
    }
}
