using System.Text.Json;
using JukeVox.Server.Models;

namespace JukeVox.Server.Services;

public class PartyService
{
    private readonly Lock _lock = new();
    private readonly string _stateFilePath;
    private readonly ILogger<PartyService> _logger;
    private Party? _currentParty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PartyService(IWebHostEnvironment env, ILogger<PartyService> logger)
    {
        _logger = logger;
        _stateFilePath = Path.Combine(env.ContentRootPath, "party-state.json");
        LoadState();
    }

    public Party? GetCurrentParty()
    {
        lock (_lock)
        {
            return _currentParty;
        }
    }

    public Party CreateParty(string hostSessionId, string inviteCode, int defaultCredits)
    {
        lock (_lock)
        {
            _currentParty = new Party
            {
                InviteCode = inviteCode,
                HostSessionId = hostSessionId,
                DefaultCredits = defaultCredits
            };
            PersistStateInternal();
            return _currentParty;
        }
    }

    public GuestSession? JoinParty(string sessionId, string inviteCode, string displayName)
    {
        lock (_lock)
        {
            if (_currentParty == null || _currentParty.InviteCode != inviteCode)
                return null;

            if (_currentParty.Guests.TryGetValue(sessionId, out var existing))
                return existing;

            var guest = new GuestSession
            {
                SessionId = sessionId,
                DisplayName = displayName,
                CreditsRemaining = _currentParty.DefaultCredits
            };
            _currentParty.Guests[sessionId] = guest;
            PersistStateInternal();
            return guest;
        }
    }

    public bool IsHost(string sessionId)
    {
        lock (_lock)
        {
            return _currentParty?.HostSessionId == sessionId;
        }
    }

    public bool IsParticipant(string sessionId)
    {
        lock (_lock)
        {
            if (_currentParty == null) return false;
            return _currentParty.HostSessionId == sessionId ||
                   _currentParty.Guests.ContainsKey(sessionId);
        }
    }

    public GuestSession? GetGuest(string sessionId)
    {
        lock (_lock)
        {
            if (_currentParty == null) return null;
            _currentParty.Guests.TryGetValue(sessionId, out var guest);
            return guest;
        }
    }

    public void UpdateSettings(string? inviteCode, int? defaultCredits)
    {
        lock (_lock)
        {
            if (_currentParty == null) return;
            if (inviteCode != null) _currentParty.InviteCode = inviteCode;
            if (defaultCredits.HasValue) _currentParty.DefaultCredits = defaultCredits.Value;
            PersistStateInternal();
        }
    }

    public void SetSpotifyTokens(SpotifyTokens tokens)
    {
        lock (_lock)
        {
            if (_currentParty != null)
            {
                _currentParty.SpotifyTokens = tokens;
                PersistStateInternal();
            }
        }
    }

    public SpotifyTokens? GetSpotifyTokens()
    {
        lock (_lock)
        {
            return _currentParty?.SpotifyTokens;
        }
    }

    public bool HasSavedParty()
    {
        lock (_lock)
        {
            return _currentParty != null;
        }
    }

    public (string InviteCode, int QueueCount, int GuestCount, DateTime CreatedAt)? GetSavedPartySummary()
    {
        lock (_lock)
        {
            if (_currentParty == null) return null;
            return (_currentParty.InviteCode, _currentParty.Queue.Count,
                    _currentParty.Guests.Count, _currentParty.CreatedAt);
        }
    }

    public Party? ResumeAsHost(string newHostSessionId)
    {
        lock (_lock)
        {
            if (_currentParty == null) return null;
            _currentParty.HostSessionId = newHostSessionId;
            PersistStateInternal();
            return _currentParty;
        }
    }

    /// <summary>
    /// Persist current party state to disk. Called by other services after mutations.
    /// </summary>
    public void PersistState()
    {
        lock (_lock)
        {
            PersistStateInternal();
        }
    }

    private void PersistStateInternal()
    {
        try
        {
            if (_currentParty == null)
            {
                if (File.Exists(_stateFilePath))
                    File.Delete(_stateFilePath);
                return;
            }

            var json = JsonSerializer.Serialize(_currentParty, JsonOptions);
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist party state to {Path}", _stateFilePath);
        }
    }

    private void LoadState()
    {
        try
        {
            if (!File.Exists(_stateFilePath)) return;

            var json = File.ReadAllText(_stateFilePath);
            _currentParty = JsonSerializer.Deserialize<Party>(json, JsonOptions);
            if (_currentParty != null)
                _logger.LogInformation("Loaded saved party state (invite code: {Code}, queue: {Count} items)",
                    _currentParty.InviteCode, _currentParty.Queue.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load party state from {Path}", _stateFilePath);
            _currentParty = null;
        }
    }
}
