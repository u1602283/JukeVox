using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

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
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

        var retrieved = _service.GetParty(party.Id);
        retrieved.Should().NotBeNull();
        retrieved.JoinToken.Should().Be(party.JoinToken);
    }

    [Test]
    public void JoinParty_CorrectCode_ReturnsGuestSession()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

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
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        var (guest1, _) = _service.JoinParty("guest-1", party.JoinToken, "Alice");
        var (guest2, _) = _service.JoinParty("guest-1", party.JoinToken, "Alice");

        guest2.Should().BeSameAs(guest1);
    }

    [Test]
    public void JoinParty_DuplicateName_ReturnsError()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var (guest, error) = _service.JoinParty("guest-2", party.JoinToken, "Alice");

        guest.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void JoinParty_DuplicateName_CaseInsensitive()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var (guest, error) = _service.JoinParty("guest-2", party.JoinToken, "alice");

        guest.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void IsHost_HostSessionId_ReturnsTrue()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

        _service.IsHost(party.Id, "host-1").Should().BeTrue();
        _service.IsHost(party.Id, "guest-1").Should().BeFalse();
    }

    [Test]
    public void IsParticipant_HostAndGuest_ReturnTrue()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        _service.IsParticipant(party.Id, "host-1").Should().BeTrue();
        _service.IsParticipant(party.Id, "guest-1").Should().BeTrue();
        _service.IsParticipant(party.Id, "stranger").Should().BeFalse();
    }

    [Test]
    public void UpdateSettings_ChangesCredits()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

        _service.UpdateSettings(party.Id, 10);

        var retrieved = _service.GetParty(party.Id)!;
        retrieved.DefaultCredits.Should().Be(10);
    }

    [Test]
    public void SpotifyTokens_RoundTrip()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

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
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

        var resumed = _service.ResumeAsHost(party.Id, "host-2");

        resumed.Should().NotBeNull();
        resumed.HostSessionId.Should().Be("host-2");
        _service.IsHost(party.Id, "host-2").Should().BeTrue();
        _service.IsHost(party.Id, "host-1").Should().BeFalse();
    }

    [Test]
    public void Persistence_SurvivesNewInstance()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var newService = CreateService(_tempDir);

        var retrieved = newService.GetParty(party.Id);
        retrieved.Should().NotBeNull();
        retrieved.JoinToken.Should().Be(party.JoinToken);
        // Guests and host session are purged on reload (ephemeral data protection
        // invalidates all session cookies on restart)
        retrieved.Guests.Should().BeEmpty();
        retrieved.HostSessionId.Should().BeEmpty();
    }

    [Test]
    public void GetAllGuests_ReturnsAllJoinedGuests()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
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
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var guest = _service.SetGuestCredits(party.Id, "guest-1", 10);

        guest.Should().NotBeNull();
        guest.CreditsRemaining.Should().Be(10);
    }

    [Test]
    public void SetGuestCredits_ClampsToZero()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var guest = _service.SetGuestCredits(party.Id, "guest-1", -3);

        guest!.CreditsRemaining.Should().Be(0);
    }

    [Test]
    public void SetGuestCredits_UnknownGuest_ReturnsNull()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

        _service.SetGuestCredits(party.Id, "nobody", 10).Should().BeNull();
    }

    [Test]
    public void AdjustAllCredits_AddsDelta()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");
        _service.JoinParty("guest-2", party.JoinToken, "Bob");

        var guests = _service.AdjustAllCredits(party.Id, 3);

        guests.Should().HaveCount(2);
        guests.Should().AllSatisfy(g => g.CreditsRemaining.Should().Be(8));
    }

    [Test]
    public void AdjustAllCredits_NegativeDelta_ClampsToZero()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var guests = _service.AdjustAllCredits(party.Id, -10);

        guests.Should().HaveCount(1);
        guests[0].CreditsRemaining.Should().Be(0);
    }

    [Test]
    public void EndParty_ClearsCurrentParty()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        _service.EndParty(party.Id);

        _service.GetParty(party.Id).Should().BeNull();
        _service.GetAllPartySummaries().Should().BeEmpty();
    }

    [Test]
    public void EndParty_DeletesStateFile()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

        _service.EndParty(party.Id);

        var newService = CreateService(_tempDir);
        newService.GetParty(party.Id).Should().BeNull();
    }

    // --- SetPartyStatus ---

    [Test]
    public void SetPartyStatus_NonexistentParty_ReturnsFalse()
    {
        _service.SetPartyStatus("nonexistent", PartyStatus.Sleeping, DateTime.UtcNow).Should().BeFalse();
    }

    [Test]
    public void SetPartyStatus_SameStatus_ReturnsFalseAndDoesNotPersist()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        party.Status.Should().Be(PartyStatus.Active);

        _service.SetPartyStatus(party.Id, PartyStatus.Active, null).Should().BeFalse();

        party.Status.Should().Be(PartyStatus.Active);
    }

    [Test]
    public void SetPartyStatus_ActiveToSleeping_UpdatesAndPersists()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        var sleepingSince = DateTime.UtcNow;

        _service.SetPartyStatus(party.Id, PartyStatus.Sleeping, sleepingSince).Should().BeTrue();

        party.Status.Should().Be(PartyStatus.Sleeping);
        party.SleepingSince.Should().Be(sleepingSince);

        // Verify persisted by reloading
        var newService = CreateService(_tempDir);
        var reloaded = newService.GetParty(party.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(PartyStatus.Sleeping);
    }

    [Test]
    public void SetPartyStatus_SleepingToActive_ClearsSleepingSince()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.SetPartyStatus(party.Id, PartyStatus.Sleeping, DateTime.UtcNow);

        _service.SetPartyStatus(party.Id, PartyStatus.Active, null).Should().BeTrue();

        party.Status.Should().Be(PartyStatus.Active);
        party.SleepingSince.Should().BeNull();
    }

    // --- TryAutoEndSleepingParty ---

    [Test]
    public void TryAutoEndSleepingParty_SleepingPastThreshold_EndsParty()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.SetPartyStatus(party.Id, PartyStatus.Sleeping, time.GetUtcNow().UtcDateTime);

        time.Advance(TimeSpan.FromMinutes(121));

        _service.TryAutoEndSleepingParty(party.Id, 120, time).Should().BeTrue();
        _service.GetParty(party.Id).Should().BeNull();
    }

    [Test]
    public void TryAutoEndSleepingParty_SleepingUnderThreshold_ReturnsFalse()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.SetPartyStatus(party.Id, PartyStatus.Sleeping, time.GetUtcNow().UtcDateTime);

        time.Advance(TimeSpan.FromMinutes(60));

        _service.TryAutoEndSleepingParty(party.Id, 120, time).Should().BeFalse();
        _service.GetParty(party.Id).Should().NotBeNull();
    }

    [Test]
    public void TryAutoEndSleepingParty_ActiveParty_ReturnsFalse()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

        _service.TryAutoEndSleepingParty(party.Id, 120, time).Should().BeFalse();
        _service.GetParty(party.Id).Should().NotBeNull();
    }

    [Test]
    public void TryAutoEndSleepingParty_WokenBetweenCheckAndEnd_ReturnsFalse()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.SetPartyStatus(party.Id, PartyStatus.Sleeping, time.GetUtcNow().UtcDateTime);

        time.Advance(TimeSpan.FromMinutes(121));

        // Simulate wake happening before TryAutoEnd checks under lock
        _service.SetPartyStatus(party.Id, PartyStatus.Active, null);

        _service.TryAutoEndSleepingParty(party.Id, 120, time).Should().BeFalse();
        _service.GetParty(party.Id).Should().NotBeNull();
    }

    // --- TrySpendCredit ---

    [Test]
    public void TrySpendCredit_ValidGuest_DecrementsAndReturnsName()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var (name, error) = _service.TrySpendCredit(party.Id, "guest-1");

        name.Should().Be("Alice");
        error.Should().BeNull();
        _service.GetGuest(party.Id, "guest-1")!.CreditsRemaining.Should().Be(4);
    }

    [Test]
    public void TrySpendCredit_NoCredits_ReturnsError()
    {
        var party = _service.CreateParty("host-1", HostId, 1).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");
        _service.TrySpendCredit(party.Id, "guest-1"); // spend the one credit

        var (name, error) = _service.TrySpendCredit(party.Id, "guest-1");

        name.Should().BeNull();
        error.Should().Be("No credits remaining");
    }

    [Test]
    public void TrySpendCredit_NotParticipant_ReturnsError()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

        var (name, error) = _service.TrySpendCredit(party.Id, "stranger");

        name.Should().BeNull();
        error.Should().Be("Not a party participant");
    }

    [Test]
    public void TrySpendCredit_NoParty_ReturnsError()
    {
        var (name, error) = _service.TrySpendCredit("nonexistent", "guest-1");

        name.Should().BeNull();
        error.Should().Be("No active party");
    }

    // --- GetPartiesForHost ---

    [Test]
    public void GetPartiesForHost_ReturnsOnlyOwnedParties()
    {
        _service.CreateParty("host-a", "host-a-id", 5);
        _service.CreateParty("host-b", "host-b-id", 5);

        _service.GetPartiesForHost("host-a-id").Should().HaveCount(1);
        _service.GetPartiesForHost("host-b-id").Should().HaveCount(1);
        _service.GetPartiesForHost("nobody").Should().BeEmpty();
    }

    // --- GetAllPartySummaries ---

    [Test]
    public void GetAllPartySummaries_ReturnsCorrectCounts()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");

        var summaries = _service.GetAllPartySummaries();

        summaries.Should().HaveCount(1);
        summaries[0].GuestCount.Should().Be(1);
        summaries[0].HostId.Should().Be(HostId);
    }

    // --- UnmapSession ---

    [Test]
    public void UnmapSession_RemovesMapping()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.GetPartyIdForSession("host-1").Should().Be(party.Id);

        _service.UnmapSession("host-1");

        _service.GetPartyIdForSession("host-1").Should().BeNull();
    }

    // --- DemoteHostToGuest ---

    [Test]
    public void DemoteHostToGuest_AddsHostAsGuest()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

        _service.DemoteHostToGuest(party.Id, "OldHost");

        var guest = _service.GetGuest(party.Id, "host-1");
        guest.Should().NotBeNull();
        guest!.DisplayName.Should().Be("OldHost");
        guest.CreditsRemaining.Should().Be(5);
    }

    [Test]
    public void DemoteHostToGuest_Idempotent_DoesNotOverwrite()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.DemoteHostToGuest(party.Id, "OldHost");
        _service.SetGuestCredits(party.Id, "host-1", 99);

        // Second demote should not overwrite existing guest
        _service.DemoteHostToGuest(party.Id, "OldHost");

        _service.GetGuest(party.Id, "host-1")!.CreditsRemaining.Should().Be(99);
    }

    // --- CreateParty atomicity ---

    [Test]
    public void CreateParty_SecondParty_ReturnsError()
    {
        _service.CreateParty("host-1", HostId, 5);

        var (party, error) = _service.CreateParty("host-1", HostId, 5);

        party.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    // --- Multi-party isolation ---

    [Test]
    public void SessionMapping_IsolatesPartiesCorrectly()
    {
        var partyA = _service.CreateParty("host-a", "host-a-id", 5).Party!;
        var partyB = _service.CreateParty("host-b", "host-b-id", 5).Party!;

        _service.JoinParty("guest-1", partyA.JoinToken, "Alice");
        _service.JoinParty("guest-2", partyB.JoinToken, "Bob");

        _service.GetPartyIdForSession("guest-1").Should().Be(partyA.Id);
        _service.GetPartyIdForSession("guest-2").Should().Be(partyB.Id);
        _service.IsParticipant(partyA.Id, "guest-2").Should().BeFalse();
        _service.IsParticipant(partyB.Id, "guest-1").Should().BeFalse();
    }

    [Test]
    public void EndParty_DoesNotAffectOtherParty()
    {
        var partyA = _service.CreateParty("host-a", "host-a-id", 5).Party!;
        var partyB = _service.CreateParty("host-b", "host-b-id", 5).Party!;
        _service.JoinParty("guest-1", partyB.JoinToken, "Bob");

        _service.EndParty(partyA.Id);

        _service.GetParty(partyB.Id).Should().NotBeNull();
        _service.GetPartyIdForSession("guest-1").Should().Be(partyB.Id);
    }

    // --- RemoveGuest session cleanup ---

    [Test]
    public void RemoveGuest_UnmapsSession()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");
        _service.GetPartyIdForSession("guest-1").Should().Be(party.Id);

        _service.RemoveGuest(party.Id, "guest-1");

        _service.GetPartyIdForSession("guest-1").Should().BeNull();
    }

    // --- JoinParty as host returns null ---

    [Test]
    public void JoinParty_AsHost_ReturnsNull()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;

        var (guest, error) = _service.JoinParty("host-1", party.JoinToken, "Host");

        guest.Should().BeNull();
        error.Should().BeNull();
    }

    // --- EndParty cleans up all session mappings ---

    [Test]
    public void EndParty_UnmapsAllSessions()
    {
        var party = _service.CreateParty("host-1", HostId, 5).Party!;
        _service.JoinParty("guest-1", party.JoinToken, "Alice");
        _service.JoinParty("guest-2", party.JoinToken, "Bob");

        _service.EndParty(party.Id);

        _service.GetPartyIdForSession("host-1").Should().BeNull();
        _service.GetPartyIdForSession("guest-1").Should().BeNull();
        _service.GetPartyIdForSession("guest-2").Should().BeNull();
    }
}
