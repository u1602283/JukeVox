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
    public void GetCurrentParty_NoParty_ReturnsNull()
    {
        _service.GetCurrentParty().Should().BeNull();
    }

    [Test]
    public void CreateParty_ReturnsPartyWithCorrectValues()
    {
        var party = _service.CreateParty("host-1", "1234", 5);

        party.Should().NotBeNull();
        party.Should().BeEquivalentTo(new
        {
            InviteCode = "1234",
            HostSessionId = "host-1",
            DefaultCredits = 5
        });
    }

    [Test]
    public void GetCurrentParty_AfterCreate_ReturnsParty()
    {
        _service.CreateParty("host-1", "1234", 5);

        var party = _service.GetCurrentParty();
        party.Should().NotBeNull();
        party!.InviteCode.Should().Be("1234");
    }

    [Test]
    public void JoinParty_CorrectCode_ReturnsGuestSession()
    {
        _service.CreateParty("host-1", "1234", 5);

        var guest = _service.JoinParty("guest-1", "1234", "Alice");

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
        _service.CreateParty("host-1", "1234", 5);

        _service.JoinParty("guest-1", "9999", "Alice").Should().BeNull();
    }

    [Test]
    public void JoinParty_IdempotentRejoin_ReturnsSameGuest()
    {
        _service.CreateParty("host-1", "1234", 5);
        var guest1 = _service.JoinParty("guest-1", "1234", "Alice");
        var guest2 = _service.JoinParty("guest-1", "1234", "Alice");

        guest2.Should().BeSameAs(guest1);
    }

    [Test]
    public void IsHost_HostSessionId_ReturnsTrue()
    {
        _service.CreateParty("host-1", "1234", 5);

        _service.IsHost("host-1").Should().BeTrue();
        _service.IsHost("guest-1").Should().BeFalse();
    }

    [Test]
    public void IsParticipant_HostAndGuest_ReturnTrue()
    {
        _service.CreateParty("host-1", "1234", 5);
        _service.JoinParty("guest-1", "1234", "Alice");

        _service.IsParticipant("host-1").Should().BeTrue();
        _service.IsParticipant("guest-1").Should().BeTrue();
        _service.IsParticipant("stranger").Should().BeFalse();
    }

    [Test]
    public void UpdateSettings_ChangesInviteCodeAndCredits()
    {
        _service.CreateParty("host-1", "1234", 5);

        _service.UpdateSettings("5678", 10);

        var party = _service.GetCurrentParty()!;
        party.InviteCode.Should().Be("5678");
        party.DefaultCredits.Should().Be(10);
    }

    [Test]
    public void SpotifyTokens_RoundTrip()
    {
        _service.CreateParty("host-1", "1234", 5);

        var tokens = new SpotifyTokens
        {
            AccessToken = "access",
            RefreshToken = "refresh",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _service.SetSpotifyTokens(tokens);

        var retrieved = _service.GetSpotifyTokens();
        retrieved.Should().NotBeNull();
        retrieved!.AccessToken.Should().Be("access");
        retrieved.RefreshToken.Should().Be("refresh");
    }

    [Test]
    public void ResumeAsHost_UpdatesHostSessionId()
    {
        _service.CreateParty("host-1", "1234", 5);

        var party = _service.ResumeAsHost("host-2");

        party.Should().NotBeNull();
        party!.HostSessionId.Should().Be("host-2");
        _service.IsHost("host-2").Should().BeTrue();
        _service.IsHost("host-1").Should().BeFalse();
    }

    [Test]
    public void Persistence_SurvivesNewInstance()
    {
        _service.CreateParty("host-1", "1234", 5);
        _service.JoinParty("guest-1", "1234", "Alice");

        var newService = CreateService(_tempDir);

        var party = newService.GetCurrentParty();
        party.Should().NotBeNull();
        party!.InviteCode.Should().Be("1234");
        party.Guests.Should().ContainKey("guest-1");
    }

    [Test]
    public void GetAllGuests_ReturnsAllJoinedGuests()
    {
        _service.CreateParty("host-1", "1234", 5);
        _service.JoinParty("guest-1", "1234", "Alice");
        _service.JoinParty("guest-2", "1234", "Bob");

        var guests = _service.GetAllGuests();

        guests.Should().HaveCount(2);
        guests.Select(g => g.DisplayName).Should().BeEquivalentTo(["Alice", "Bob"]);
    }

    [Test]
    public void GetAllGuests_NoParty_ReturnsEmpty()
    {
        _service.GetAllGuests().Should().BeEmpty();
    }

    [Test]
    public void SetGuestCredits_SetsAbsoluteValue()
    {
        _service.CreateParty("host-1", "1234", 5);
        _service.JoinParty("guest-1", "1234", "Alice");

        var guest = _service.SetGuestCredits("guest-1", 10);

        guest.Should().NotBeNull();
        guest!.CreditsRemaining.Should().Be(10);
    }

    [Test]
    public void SetGuestCredits_ClampsToZero()
    {
        _service.CreateParty("host-1", "1234", 5);
        _service.JoinParty("guest-1", "1234", "Alice");

        var guest = _service.SetGuestCredits("guest-1", -3);

        guest!.CreditsRemaining.Should().Be(0);
    }

    [Test]
    public void SetGuestCredits_UnknownGuest_ReturnsNull()
    {
        _service.CreateParty("host-1", "1234", 5);

        _service.SetGuestCredits("nobody", 10).Should().BeNull();
    }

    [Test]
    public void AdjustAllCredits_AddsDelta()
    {
        _service.CreateParty("host-1", "1234", 5);
        _service.JoinParty("guest-1", "1234", "Alice");
        _service.JoinParty("guest-2", "1234", "Bob");

        var guests = _service.AdjustAllCredits(3);

        guests.Should().HaveCount(2);
        guests.Should().AllSatisfy(g => g.CreditsRemaining.Should().Be(8));
    }

    [Test]
    public void AdjustAllCredits_NegativeDelta_ClampsToZero()
    {
        _service.CreateParty("host-1", "1234", 5);
        _service.JoinParty("guest-1", "1234", "Alice");

        var guests = _service.AdjustAllCredits(-10);

        guests.Should().HaveCount(1);
        guests[0].CreditsRemaining.Should().Be(0);
    }

    [Test]
    public void EndParty_ClearsCurrentParty()
    {
        _service.CreateParty("host-1", "1234", 5);
        _service.JoinParty("guest-1", "1234", "Alice");

        _service.EndParty();

        _service.GetCurrentParty().Should().BeNull();
        _service.HasSavedParty().Should().BeFalse();
    }

    [Test]
    public void EndParty_DeletesStateFile()
    {
        _service.CreateParty("host-1", "1234", 5);

        _service.EndParty();

        var newService = CreateService(_tempDir);
        newService.GetCurrentParty().Should().BeNull();
    }
}
