using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JukeVox.Server.Models;

namespace JukeVox.Server.Services;

public class HostCredentialService
{
    private readonly string _credentialsDir;
    private readonly string _inviteCodesFilePath;
    private readonly ILogger<HostCredentialService> _logger;
    private readonly Lock _lock = new();

    private readonly ConcurrentDictionary<string, HostCredential> _credentials = new();
    private readonly ConcurrentDictionary<string, string> _credentialIdToHostId = new(new ByteArrayKeyComparer());
    private readonly ConcurrentDictionary<string, DateTime> _inviteCodes = new();
    private string? _setupToken;

    // Temporary challenge storage for WebAuthn ceremonies
    private readonly ConcurrentDictionary<string, (object Options, DateTime Created)> _pendingChallenges = new();

    private static readonly string[] DictionaryPaths =
    [
        "/usr/share/dict/words",
        "/usr/share/dict/british"
    ];
    private const string SpecialChars = "!@#$%^&*+=?~";
    private static readonly TimeSpan InviteCodeTtl = TimeSpan.FromHours(24);

    private static readonly Lazy<string[]> DictionaryWords = new(() =>
    {
        var path = DictionaryPaths.FirstOrDefault(File.Exists);
        if (path == null)
            return [];

        return File.ReadAllLines(path)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= 5 && w.All(c => c >= 'a' && c <= 'z'))
            .Distinct()
            .ToArray();
    });

    public HostCredentialService(IWebHostEnvironment env, ILogger<HostCredentialService> logger)
    {
        _logger = logger;
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR") ?? env.ContentRootPath;
        _credentialsDir = Path.Combine(dataDir, "host-credentials");
        _inviteCodesFilePath = Path.Combine(_credentialsDir, "_invite-codes.json");
        Directory.CreateDirectory(_credentialsDir);
        LoadCredentials();
        MigrateLegacyCredential(dataDir);
        LoadInviteCodes();
    }

    public bool HasAnyCredential => !_credentials.IsEmpty;

    public bool IsSetupAvailable => !HasAnyCredential && _setupToken != null;

    public HostCredential? GetCredential(string hostId)
    {
        _credentials.TryGetValue(hostId, out var credential);
        return credential;
    }

    public HostCredential? GetCredentialByCredentialId(byte[] credentialId)
    {
        var key = Convert.ToBase64String(credentialId);
        if (_credentialIdToHostId.TryGetValue(key, out var hostId))
            return GetCredential(hostId);
        return null;
    }

    public HostCredential? GetCredentialByCredentialIdString(string credentialIdBase64)
    {
        // Fido2 v4 uses base64url strings for credential IDs — normalize to standard base64 for lookup
        var normalized = Convert.ToBase64String(Convert.FromBase64String(
            credentialIdBase64.Replace('-', '+').Replace('_', '/').PadRight(
                credentialIdBase64.Length + (4 - credentialIdBase64.Length % 4) % 4, '=')));
        if (_credentialIdToHostId.TryGetValue(normalized, out var hostId))
            return GetCredential(hostId);
        return null;
    }

    public List<HostCredential> GetAllCredentials()
    {
        return [.. _credentials.Values];
    }

    public bool IsSetupTokenValid(string token)
    {
        if (_setupToken == null) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(_setupToken));
    }

    public void SaveCredential(HostCredential credential)
    {
        lock (_lock)
        {
            _credentials[credential.HostId] = credential;
            _credentialIdToHostId[Convert.ToBase64String(credential.CredentialId)] = credential.HostId;

            if (credential.IsAdmin)
                _setupToken = null;

            var json = JsonSerializer.Serialize(new HostCredentialJson
            {
                HostId = credential.HostId,
                DisplayName = credential.DisplayName,
                CredentialId = Convert.ToBase64String(credential.CredentialId),
                PublicKey = Convert.ToBase64String(credential.PublicKey),
                SignCount = credential.SignCount,
                IsAdmin = credential.IsAdmin,
                CreatedAt = credential.CreatedAt
            }, new JsonSerializerOptions { WriteIndented = true });

            var filePath = Path.Combine(_credentialsDir, $"{credential.HostId}.json");
            var tmpPath = filePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, filePath, overwrite: true);
            _logger.LogInformation("Host credential saved for {HostId} ({DisplayName})", credential.HostId, credential.DisplayName);
        }
    }

    public void UpdateSignCount(string hostId, uint newSignCount)
    {
        var credential = GetCredential(hostId);
        if (credential == null) return;

        lock (_lock)
        {
            credential.SignCount = newSignCount;
        }
        SaveCredential(credential);
    }

    public bool IsAdmin(string hostId)
    {
        return _credentials.TryGetValue(hostId, out var cred) && cred.IsAdmin;
    }

    public bool DeleteCredential(string hostId)
    {
        lock (_lock)
        {
            if (!_credentials.TryRemove(hostId, out var credential))
                return false;

            _credentialIdToHostId.TryRemove(Convert.ToBase64String(credential.CredentialId), out _);

            var filePath = Path.Combine(_credentialsDir, $"{hostId}.json");
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete credential file for {HostId}", hostId);
            }

            _logger.LogInformation("Host credential deleted: {HostId} ({DisplayName})", hostId, credential.DisplayName);
            return true;
        }
    }

    // --- Invite codes ---

    public string GenerateInviteCode()
    {
        lock (_lock)
        {
            // Only one invite code valid at a time — clear any existing
            _inviteCodes.Clear();

            var code = Guid.NewGuid().ToString("N")[..16];
            _inviteCodes[code] = DateTime.UtcNow;
            PersistInviteCodes();
            _logger.LogInformation("Generated host invite code");
            return code;
        }
    }

    public bool IsInviteCodeValid(string code)
    {
        lock (_lock)
        {
            if (!_inviteCodes.TryGetValue(code, out var created))
                return false;
            return DateTime.UtcNow - created <= InviteCodeTtl;
        }
    }

    public bool ValidateAndConsumeInviteCode(string code)
    {
        lock (_lock)
        {
            if (!_inviteCodes.TryRemove(code, out var created))
                return false;

            if (DateTime.UtcNow - created > InviteCodeTtl)
                return false;

            PersistInviteCodes();
            return true;
        }
    }

    // --- Challenge storage ---

    public void StorePendingChallenge(string sessionId, object options)
    {
        CleanupExpiredChallenges();
        _pendingChallenges[sessionId] = (options, DateTime.UtcNow);
    }

    public T? GetPendingChallenge<T>(string sessionId) where T : class
    {
        if (_pendingChallenges.TryRemove(sessionId, out var entry))
        {
            if (DateTime.UtcNow - entry.Created < TimeSpan.FromMinutes(5))
                return entry.Options as T;
        }
        return null;
    }

    // --- Private ---

    private void CleanupExpiredChallenges()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        foreach (var key in _pendingChallenges.Keys)
        {
            if (_pendingChallenges.TryGetValue(key, out var entry) && entry.Created < cutoff)
                _pendingChallenges.TryRemove(key, out _);
        }
    }

    private void LoadCredentials()
    {
        var files = Directory.GetFiles(_credentialsDir, "*.json")
            .Where(f => !Path.GetFileName(f).StartsWith('_'));

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var data = JsonSerializer.Deserialize<HostCredentialJson>(json);
                if (data != null)
                {
                    var credential = new HostCredential
                    {
                        HostId = data.HostId,
                        DisplayName = data.DisplayName,
                        CredentialId = Convert.FromBase64String(data.CredentialId),
                        PublicKey = Convert.FromBase64String(data.PublicKey),
                        SignCount = data.SignCount,
                        IsAdmin = data.IsAdmin,
                        CreatedAt = data.CreatedAt ?? DateTime.UtcNow
                    };
                    _credentials[credential.HostId] = credential;
                    _credentialIdToHostId[Convert.ToBase64String(credential.CredentialId)] = credential.HostId;
                    _logger.LogInformation("Loaded host credential: {HostId} ({DisplayName})", credential.HostId, credential.DisplayName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load host credential from {Path}", file);
            }
        }

        if (_credentials.IsEmpty)
        {
            _setupToken = GeneratePassphrase();
            _logger.LogWarning("No host credentials found. Setup mode active.");
            _logger.LogWarning("Register your passkey at /host/setup using this token:");
            _logger.LogWarning("");
            _logger.LogWarning("    {Token}", _setupToken);
            _logger.LogWarning("");
        }
        else
        {
            _logger.LogInformation("Loaded {Count} host credential(s)", _credentials.Count);
        }
    }

    private void MigrateLegacyCredential(string dataDir)
    {
        var legacyPath = Path.Combine(dataDir, "host-credential.json");
        if (!File.Exists(legacyPath) || HasAnyCredential)
            return;

        try
        {
            var json = File.ReadAllText(legacyPath);
            var data = JsonSerializer.Deserialize<LegacyHostCredentialJson>(json);
            if (data != null)
            {
                var hostId = Guid.NewGuid().ToString("N")[..8];
                var credential = new HostCredential
                {
                    HostId = hostId,
                    DisplayName = "Admin",
                    CredentialId = Convert.FromBase64String(data.CredentialId),
                    PublicKey = Convert.FromBase64String(data.PublicKey),
                    SignCount = data.SignCount,
                    IsAdmin = true
                };
                SaveCredential(credential);
                File.Move(legacyPath, legacyPath + ".migrated");
                _setupToken = null;
                _logger.LogInformation("Migrated legacy host credential to multi-host format (hostId: {HostId})", hostId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate legacy host credential from {Path}", legacyPath);
        }
    }

    private void LoadInviteCodes()
    {
        if (!File.Exists(_inviteCodesFilePath))
            return;

        try
        {
            var json = File.ReadAllText(_inviteCodesFilePath);
            var codes = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
            if (codes != null)
            {
                var cutoff = DateTime.UtcNow - InviteCodeTtl;
                foreach (var kv in codes)
                {
                    if (kv.Value >= cutoff)
                        _inviteCodes[kv.Key] = kv.Value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load invite codes");
        }
    }

    private void PersistInviteCodes()
    {
        try
        {
            var json = JsonSerializer.Serialize(_inviteCodes.ToDictionary(kv => kv.Key, kv => kv.Value));
            File.WriteAllText(_inviteCodesFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist invite codes");
        }
    }

    private string GeneratePassphrase()
    {
        var words = DictionaryWords.Value;
        if (words.Length == 0)
        {
            _logger.LogWarning(
                "Dictionary not found at any of [{Paths}]. Install the 'words' package. Using random fallback token.",
                string.Join(", ", DictionaryPaths));
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
        }

        var sb = new StringBuilder();
        for (var i = 0; i < 4; i++)
        {
            sb.Append(words[RandomNumberGenerator.GetInt32(words.Length)]);
            sb.Append(RandomNumberGenerator.GetInt32(100));
            sb.Append(SpecialChars[RandomNumberGenerator.GetInt32(SpecialChars.Length)]);
        }
        return sb.ToString();
    }

    private class HostCredentialJson
    {
        public string HostId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string CredentialId { get; set; } = "";
        public string PublicKey { get; set; } = "";
        public uint SignCount { get; set; }
        public bool IsAdmin { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    private class LegacyHostCredentialJson
    {
        public string CredentialId { get; set; } = "";
        public string PublicKey { get; set; } = "";
        public uint SignCount { get; set; }
    }

    /// <summary>
    /// Comparer for using base64-encoded byte arrays as dictionary keys.
    /// </summary>
    private class ByteArrayKeyComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);
        public int GetHashCode(string obj) => obj.GetHashCode(StringComparison.Ordinal);
    }
}
