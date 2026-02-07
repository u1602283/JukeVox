using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using DnsClient;
using DnsClient.Protocol;
using Fido2NetLib;
using JukeVox.Server.Models;

namespace JukeVox.Server.Services;

public class HostCredentialService
{
    private readonly string _credentialFilePath;
    private readonly string _serverDomain;
    private readonly ILogger<HostCredentialService> _logger;
    private readonly Lock _lock = new();
    private HostCredential? _credential;
    private string? _setupToken;

    // Temporary challenge storage for WebAuthn ceremonies
    private readonly ConcurrentDictionary<string, (object Options, DateTime Created)> _pendingChallenges = new();

    private static readonly string[] Adjectives =
    [
        "Amber", "Bold", "Calm", "Crimson", "Dark", "Eager", "Fading", "Golden",
        "Hollow", "Iron", "Jade", "Keen", "Lunar", "Misty", "Noble", "Opal",
        "Pale", "Quiet", "Rapid", "Silver", "Tidal", "Ultra", "Vivid", "Wild"
    ];

    private static readonly string[] Nouns =
    [
        "Arrow", "Beacon", "Cedar", "Drift", "Eagle", "Falcon", "Grove", "Harbor",
        "Ivory", "Jungle", "Kite", "Lantern", "Mesa", "Nebula", "Orbit", "Plasma",
        "Quartz", "Ridge", "Storm", "Tiger", "Umbra", "Vortex", "Willow", "Zenith"
    ];

    public HostCredentialService(IWebHostEnvironment env, ILogger<HostCredentialService> logger, Fido2Configuration fido2Config)
    {
        _logger = logger;
        _serverDomain = fido2Config.ServerDomain;
        _credentialFilePath = Path.Combine(env.ContentRootPath, "host-credential.json");
        LoadCredential();
    }

    public bool HasCredential
    {
        get { lock (_lock) { return _credential != null; } }
    }

    public bool IsSetupAvailable => !HasCredential && _setupToken != null;

    public HostCredential? GetCredential()
    {
        lock (_lock) { return _credential; }
    }

    public bool IsSetupTokenValid(string token)
    {
        if (_setupToken == null) return false;
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(token),
            System.Text.Encoding.UTF8.GetBytes(_setupToken));
    }

    public string SaveCredential(HostCredential credential)
    {
        lock (_lock)
        {
            _credential = credential;
            _setupToken = null; // Registration complete — clear the token

            var json = JsonSerializer.Serialize(new HostCredentialJson
            {
                CredentialId = Convert.ToBase64String(credential.CredentialId),
                PublicKey = Convert.ToBase64String(credential.PublicKey),
                SignCount = credential.SignCount
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_credentialFilePath, json);
            _logger.LogInformation("Host credential saved to {Path}. Setup token cleared.", _credentialFilePath);

            // Return DNS TXT record value for display
            var credIdB64Url = Base64UrlEncode(credential.CredentialId);
            var pubKeyB64Url = Base64UrlEncode(credential.PublicKey);
            return $"v=jukevox1;credId={credIdB64Url};pubKey={pubKeyB64Url};sigCount={credential.SignCount}";
        }
    }

    public void UpdateSignCount(uint newSignCount)
    {
        lock (_lock)
        {
            if (_credential == null) return;
            _credential.SignCount = newSignCount;
        }
        // Re-save outside the main lock (SaveCredential acquires its own lock)
        if (_credential != null) SaveCredential(_credential);
    }

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

    private void CleanupExpiredChallenges()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        foreach (var key in _pendingChallenges.Keys)
        {
            if (_pendingChallenges.TryGetValue(key, out var entry) && entry.Created < cutoff)
                _pendingChallenges.TryRemove(key, out _);
        }
    }

    private void LoadCredential()
    {
        // Try local file
        if (File.Exists(_credentialFilePath))
        {
            try
            {
                var json = File.ReadAllText(_credentialFilePath);
                var data = JsonSerializer.Deserialize<HostCredentialJson>(json);
                if (data != null)
                {
                    _credential = new HostCredential
                    {
                        CredentialId = Convert.FromBase64String(data.CredentialId),
                        PublicKey = Convert.FromBase64String(data.PublicKey),
                        SignCount = data.SignCount
                    };
                    _logger.LogInformation("Loaded host credential from file");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load host credential from {Path}", _credentialFilePath);
            }
        }

        // Try DNS TXT record
        if (TryLoadFromDns())
            return;

        // No credential found — generate a setup token for first-time registration
        _setupToken = GeneratePassphrase();
        _logger.LogWarning("No host credential found. Setup mode active.");
        _logger.LogWarning("Register your passkey at /host/setup using this token:");
        _logger.LogWarning("");
        _logger.LogWarning("    {Token}", _setupToken);
        _logger.LogWarning("");
    }

    private static string GeneratePassphrase()
    {
        // Format: Adjective-Noun-Number-Adjective-Noun
        // Entropy: ~24 * ~24 * 90 * ~24 * ~24 ≈ 28M combinations
        var adj1 = Adjectives[RandomNumberGenerator.GetInt32(Adjectives.Length)];
        var noun1 = Nouns[RandomNumberGenerator.GetInt32(Nouns.Length)];
        var num = RandomNumberGenerator.GetInt32(10, 100);
        var adj2 = Adjectives[RandomNumberGenerator.GetInt32(Adjectives.Length)];
        var noun2 = Nouns[RandomNumberGenerator.GetInt32(Nouns.Length)];
        return $"{adj1}-{noun1}-{num}-{adj2}-{noun2}";
    }

    private bool TryLoadFromDns()
    {
        var recordName = $"_jukevox-auth.{_serverDomain}";
        try
        {
            var lookup = new LookupClient();
            var result = lookup.Query(recordName, QueryType.TXT);

            foreach (var txt in result.Answers.TxtRecords())
            {
                var value = string.Join("", txt.Text);
                if (TryParseCredentialRecord(value, out var credential))
                {
                    _credential = credential;
                    _logger.LogInformation("Loaded host credential from DNS TXT record ({Record})", recordName);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNS TXT record lookup failed for {Record}", recordName);
        }
        return false;
    }

    private static bool TryParseCredentialRecord(string value, out HostCredential? credential)
    {
        credential = null;
        if (!value.StartsWith("v=jukevox1;"))
            return false;

        var parts = value.Split(';')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);

        if (!parts.TryGetValue("credId", out var credId) ||
            !parts.TryGetValue("pubKey", out var pubKey) ||
            !parts.TryGetValue("sigCount", out var sigCountStr) ||
            !uint.TryParse(sigCountStr, out var sigCount))
            return false;

        try
        {
            credential = new HostCredential
            {
                CredentialId = Base64UrlDecode(credId),
                PublicKey = Base64UrlDecode(pubKey),
                SignCount = sigCount
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }

    private class HostCredentialJson
    {
        public string CredentialId { get; set; } = "";
        public string PublicKey { get; set; } = "";
        public uint SignCount { get; set; }
    }
}
