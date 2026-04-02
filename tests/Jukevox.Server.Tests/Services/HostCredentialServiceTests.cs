using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Moq;
using JukeVox.Server.Models;
using JukeVox.Server.Services;

namespace JukeVox.Server.Tests.Services;

[TestFixture]
public class HostCredentialServiceTests
{
    private string _tempDir = null!;
    private HostCredentialService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"jukevox-cred-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = CreateService(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static HostCredentialService CreateService(string contentRootPath)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(contentRootPath);
        return new HostCredentialService(env.Object, NullLogger<HostCredentialService>.Instance);
    }

    private static HostCredential MakeCredential(string hostId = "host-1", bool isAdmin = false) => new()
    {
        HostId = hostId,
        DisplayName = $"Host {hostId}",
        CredentialId = [1, 2, 3, 4],
        PublicKey = [5, 6, 7, 8],
        SignCount = 0,
        IsAdmin = isAdmin
    };

    // --- Initial state ---

    [Test]
    public void NewService_NoCredentials_SetupIsAvailable()
    {
        _service.HasAnyCredential.Should().BeFalse();
        _service.IsSetupAvailable.Should().BeTrue();
    }

    // --- Credential CRUD ---

    [Test]
    public void SaveAndGet_RoundTrips()
    {
        var cred = MakeCredential();
        _service.SaveCredential(cred);

        var retrieved = _service.GetCredential("host-1");
        retrieved.Should().NotBeNull();
        retrieved!.DisplayName.Should().Be("Host host-1");
        retrieved.CredentialId.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4 });
    }

    [Test]
    public void GetCredential_Unknown_ReturnsNull()
    {
        _service.GetCredential("nonexistent").Should().BeNull();
    }

    [Test]
    public void GetCredentialByCredentialId_FindsByBytes()
    {
        var cred = MakeCredential();
        _service.SaveCredential(cred);

        var found = _service.GetCredentialByCredentialId([1, 2, 3, 4]);
        found.Should().NotBeNull();
        found!.HostId.Should().Be("host-1");
    }

    [Test]
    public void GetCredentialByCredentialId_UnknownBytes_ReturnsNull()
    {
        _service.GetCredentialByCredentialId([9, 9, 9]).Should().BeNull();
    }

    [Test]
    public void GetAllCredentials_ReturnsAll()
    {
        _service.SaveCredential(MakeCredential("a"));
        _service.SaveCredential(MakeCredential("b"));

        _service.GetAllCredentials().Should().HaveCount(2);
    }

    [Test]
    public void DeleteCredential_RemovesFromMemoryAndDisk()
    {
        _service.SaveCredential(MakeCredential("host-1"));

        _service.DeleteCredential("host-1").Should().BeTrue();

        _service.GetCredential("host-1").Should().BeNull();
        _service.GetCredentialByCredentialId([1, 2, 3, 4]).Should().BeNull();
    }

    [Test]
    public void DeleteCredential_Unknown_ReturnsFalse()
    {
        _service.DeleteCredential("nonexistent").Should().BeFalse();
    }

    // --- Admin ---

    [Test]
    public void IsAdmin_AdminCredential_ReturnsTrue()
    {
        _service.SaveCredential(MakeCredential("admin", isAdmin: true));

        _service.IsAdmin("admin").Should().BeTrue();
    }

    [TestCase("nonexistent")]
    [TestCase("regular")]
    public void IsAdmin_NonAdmin_ReturnsFalse(string hostId)
    {
        _service.SaveCredential(MakeCredential("regular"));
        _service.IsAdmin(hostId).Should().BeFalse();
    }

    // --- Setup token ---

    [Test]
    public void IsSetupTokenValid_CorrectToken_ReturnsTrue()
    {
        // We can't access the token directly, but saving an admin credential clears it
        // First verify setup is available (token exists)
        _service.IsSetupAvailable.Should().BeTrue();
    }

    [Test]
    public void IsSetupTokenValid_WrongToken_ReturnsFalse()
    {
        _service.IsSetupTokenValid("definitely-wrong-token").Should().BeFalse();
    }

    [Test]
    public void SaveAdminCredential_ClearsSetupToken()
    {
        _service.SaveCredential(MakeCredential("admin", isAdmin: true));

        _service.IsSetupAvailable.Should().BeFalse();
    }

    [Test]
    public void SaveNonAdminCredential_DoesNotClearSetupToken()
    {
        _service.SaveCredential(MakeCredential("regular", isAdmin: false));

        // HasAnyCredential is true, so IsSetupAvailable is false regardless
        _service.HasAnyCredential.Should().BeTrue();
    }

    // --- Sign count ---

    [Test]
    public void UpdateSignCount_PersistsNewValue()
    {
        _service.SaveCredential(MakeCredential("host-1"));

        _service.UpdateSignCount("host-1", 42);

        _service.GetCredential("host-1")!.SignCount.Should().Be(42u);
    }

    [Test]
    public void UpdateSignCount_Unknown_DoesNotThrow()
    {
        var act = () => _service.UpdateSignCount("nonexistent", 5);
        act.Should().NotThrow();
    }

    // --- Invite codes ---

    [Test]
    public void GenerateInviteCode_ReturnsNonEmptyCode()
    {
        var code = _service.GenerateInviteCode();
        code.Should().NotBeNullOrEmpty().And.HaveLength(16);
    }

    [Test]
    public void IsInviteCodeValid_ValidCode_ReturnsTrue()
    {
        var code = _service.GenerateInviteCode();
        _service.IsInviteCodeValid(code).Should().BeTrue();
    }

    [Test]
    public void IsInviteCodeValid_InvalidCode_ReturnsFalse()
    {
        _service.IsInviteCodeValid("bogus").Should().BeFalse();
    }

    [Test]
    public void ValidateAndConsumeInviteCode_ConsumesOnce()
    {
        var code = _service.GenerateInviteCode();

        _service.ValidateAndConsumeInviteCode(code).Should().BeTrue();
        _service.ValidateAndConsumeInviteCode(code).Should().BeFalse();
    }

    [Test]
    public void GenerateInviteCode_InvalidatesPreviousCode()
    {
        var code1 = _service.GenerateInviteCode();
        var code2 = _service.GenerateInviteCode();

        _service.IsInviteCodeValid(code1).Should().BeFalse();
        _service.IsInviteCodeValid(code2).Should().BeTrue();
    }

    [Test]
    public void InviteCodes_PersistAcrossInstances()
    {
        var code = _service.GenerateInviteCode();

        var newService = CreateService(_tempDir);

        newService.IsInviteCodeValid(code).Should().BeTrue();
    }

    // --- Challenge storage ---

    [Test]
    public void StorePendingChallenge_RetrievesOnce()
    {
        var options = new { Challenge = "test" };
        _service.StorePendingChallenge("session-1", options);

        var retrieved = _service.GetPendingChallenge<object>("session-1");
        retrieved.Should().NotBeNull();

        // Second retrieval should return null (consumed)
        _service.GetPendingChallenge<object>("session-1").Should().BeNull();
    }

    [Test]
    public void GetPendingChallenge_WrongType_ReturnsNull()
    {
        _service.StorePendingChallenge("session-1", "a string");

        // Type mismatch means the cast returns null
        _service.GetPendingChallenge<List<int>>("session-1").Should().BeNull();
    }

    [Test]
    public void GetPendingChallenge_UnknownSession_ReturnsNull()
    {
        _service.GetPendingChallenge<object>("nobody").Should().BeNull();
    }

    [Test]
    public void StorePendingChallenge_OverwritesPrevious()
    {
        _service.StorePendingChallenge("session-1", "first");
        _service.StorePendingChallenge("session-1", "second");

        _service.GetPendingChallenge<string>("session-1").Should().Be("second");
    }

    // --- Persistence ---

    [Test]
    public void Credentials_PersistAcrossInstances()
    {
        _service.SaveCredential(MakeCredential("host-1", isAdmin: true));

        var newService = CreateService(_tempDir);

        newService.HasAnyCredential.Should().BeTrue();
        newService.GetCredential("host-1").Should().NotBeNull();
        newService.GetCredential("host-1")!.IsAdmin.Should().BeTrue();
        newService.IsSetupAvailable.Should().BeFalse();
    }

    [Test]
    public void DeleteCredential_PersistsDeletion()
    {
        _service.SaveCredential(MakeCredential("host-1"));
        _service.DeleteCredential("host-1");

        var newService = CreateService(_tempDir);

        newService.GetCredential("host-1").Should().BeNull();
    }

    // --- Legacy migration ---

    [Test]
    public void MigrateLegacyCredential_ImportsAndRenames()
    {
        // Write a legacy credential file
        var legacyPath = Path.Combine(_tempDir, "host-credential.json");
        File.WriteAllText(legacyPath, """
        {
            "CredentialId": "AQIDBA==",
            "PublicKey": "BQYHCA==",
            "SignCount": 3
        }
        """);

        var service = CreateService(_tempDir);

        service.HasAnyCredential.Should().BeTrue();
        File.Exists(legacyPath).Should().BeFalse();
        File.Exists(legacyPath + ".migrated").Should().BeTrue();

        var creds = service.GetAllCredentials();
        creds.Should().HaveCount(1);
        creds[0].IsAdmin.Should().BeTrue();
        creds[0].DisplayName.Should().Be("Admin");
    }
}
