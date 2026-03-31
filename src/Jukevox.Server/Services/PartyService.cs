using System.Collections.Concurrent;
using System.Text.Json;
using JukeVox.Server.Models;

namespace JukeVox.Server.Services;

public class PartyService : IPartyService
{
    private readonly ConcurrentDictionary<string, Party> _parties = new();
    private readonly ConcurrentDictionary<string, string> _sessionToPartyId = new();
    private readonly ConcurrentDictionary<string, Lock> _partyLocks = new();
    private readonly string _partiesDir;
    private readonly ILogger<PartyService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PartyService(IWebHostEnvironment env, ILogger<PartyService> logger)
    {
        _logger = logger;
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR") ?? env.ContentRootPath;
        _partiesDir = Path.Combine(dataDir, "parties");
        Directory.CreateDirectory(_partiesDir);
        MigrateLegacyState(dataDir);
        LoadAllParties();
    }

    public Party? GetParty(string partyId)
    {
        _parties.TryGetValue(partyId, out var party);
        return party;
    }

    public string? GetPartyIdForSession(string sessionId)
    {
        _sessionToPartyId.TryGetValue(sessionId, out var partyId);
        return partyId;
    }

    public List<Party> GetAllParties()
    {
        return [.. _parties.Values];
    }

    public List<Party> GetPartiesForHost(string hostId)
    {
        return _parties.Values.Where(p => p.HostId == hostId).ToList();
    }

    public Party CreateParty(string hostSessionId, string hostId, string inviteCode, int defaultCredits)
    {
        var party = new Party
        {
            InviteCode = inviteCode,
            HostSessionId = hostSessionId,
            HostId = hostId,
            DefaultCredits = defaultCredits
        };

        var partyLock = GetPartyLock(party.Id);
        lock (partyLock)
        {
            _parties[party.Id] = party;
            MapSession(hostSessionId, party.Id);
            PersistStateInternal(party);
        }

        _logger.LogInformation("Party created: {PartyId} (invite: {InviteCode}, host: {HostId})",
            party.Id, inviteCode, hostId);
        return party;
    }

    public GuestSession? JoinParty(string sessionId, string inviteCode, string displayName)
    {
        // Find party by invite code
        var party = _parties.Values.FirstOrDefault(p => p.InviteCode == inviteCode);
        if (party == null) return null;

        var partyLock = GetPartyLock(party.Id);
        lock (partyLock)
        {
            if (party.Guests.TryGetValue(sessionId, out var existing))
            {
                MapSession(sessionId, party.Id);
                return existing;
            }

            var guest = new GuestSession
            {
                SessionId = sessionId,
                DisplayName = displayName,
                CreditsRemaining = party.DefaultCredits
            };
            party.Guests[sessionId] = guest;
            MapSession(sessionId, party.Id);
            PersistStateInternal(party);
            return guest;
        }
    }

    public bool IsHost(string partyId, string sessionId)
    {
        var party = GetParty(partyId);
        return party?.HostSessionId == sessionId;
    }

    public bool IsParticipant(string partyId, string sessionId)
    {
        var party = GetParty(partyId);
        if (party == null) return false;
        return party.HostSessionId == sessionId || party.Guests.ContainsKey(sessionId);
    }

    public GuestSession? GetGuest(string partyId, string sessionId)
    {
        var party = GetParty(partyId);
        if (party == null) return null;
        party.Guests.TryGetValue(sessionId, out var guest);
        return guest;
    }

    public void UpdateSettings(string partyId, string? inviteCode, int? defaultCredits)
    {
        var party = GetParty(partyId);
        if (party == null) return;

        var partyLock = GetPartyLock(partyId);
        lock (partyLock)
        {
            if (inviteCode != null) party.InviteCode = inviteCode;
            if (defaultCredits.HasValue) party.DefaultCredits = defaultCredits.Value;
            PersistStateInternal(party);
        }
    }

    public void SetSpotifyTokens(string partyId, SpotifyTokens tokens)
    {
        var party = GetParty(partyId);
        if (party == null) return;

        var partyLock = GetPartyLock(partyId);
        lock (partyLock)
        {
            party.SpotifyTokens = tokens;
            PersistStateInternal(party);
        }
    }

    public SpotifyTokens? GetSpotifyTokens(string partyId)
    {
        return GetParty(partyId)?.SpotifyTokens;
    }

    public List<(string PartyId, string InviteCode, string HostId, int QueueCount, int GuestCount, DateTime CreatedAt)> GetAllPartySummaries()
    {
        return _parties.Values.Select(p =>
            (p.Id, p.InviteCode, p.HostId, p.Queue.Count, p.Guests.Count, p.CreatedAt)
        ).ToList();
    }

    public Party? ResumeAsHost(string partyId, string newHostSessionId)
    {
        var party = GetParty(partyId);
        if (party == null) return null;

        var partyLock = GetPartyLock(partyId);
        lock (partyLock)
        {
            // Unmap old host session
            if (!string.IsNullOrEmpty(party.HostSessionId))
                _sessionToPartyId.TryRemove(party.HostSessionId, out _);

            party.HostSessionId = newHostSessionId;
            MapSession(newHostSessionId, partyId);
            PersistStateInternal(party);
            return party;
        }
    }

    public List<GuestSession> GetAllGuests(string partyId)
    {
        var party = GetParty(partyId);
        if (party == null) return [];
        return party.Guests.Values.ToList();
    }

    public GuestSession? SetGuestCredits(string partyId, string sessionId, int credits)
    {
        var party = GetParty(partyId);
        if (party == null) return null;

        var partyLock = GetPartyLock(partyId);
        lock (partyLock)
        {
            if (!party.Guests.TryGetValue(sessionId, out var guest)) return null;
            guest.CreditsRemaining = Math.Max(0, credits);
            PersistStateInternal(party);
            return guest;
        }
    }

    public List<GuestSession> AdjustAllCredits(string partyId, int delta)
    {
        var party = GetParty(partyId);
        if (party == null) return [];

        var partyLock = GetPartyLock(partyId);
        lock (partyLock)
        {
            foreach (var guest in party.Guests.Values)
            {
                guest.CreditsRemaining = Math.Max(0, guest.CreditsRemaining + delta);
            }
            PersistStateInternal(party);
            return party.Guests.Values.ToList();
        }
    }

    public bool RemoveGuest(string partyId, string sessionId)
    {
        var party = GetParty(partyId);
        if (party == null) return false;

        var partyLock = GetPartyLock(partyId);
        lock (partyLock)
        {
            if (!party.Guests.Remove(sessionId)) return false;
            _sessionToPartyId.TryRemove(sessionId, out _);
            PersistStateInternal(party);
            return true;
        }
    }

    public void EndParty(string partyId)
    {
        if (!_parties.TryRemove(partyId, out _)) return;

        // Clean up session mappings for this party
        var sessionsToRemove = _sessionToPartyId
            .Where(kv => kv.Value == partyId)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var sessionId in sessionsToRemove)
            _sessionToPartyId.TryRemove(sessionId, out _);

        _partyLocks.TryRemove(partyId, out _);

        // Delete persistence file
        var filePath = Path.Combine(_partiesDir, $"{partyId}.json");
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete party state file for {PartyId}", partyId);
        }

        _logger.LogInformation("Party ended: {PartyId}", partyId);
    }

    public void PersistState(string partyId)
    {
        var party = GetParty(partyId);
        if (party == null) return;

        var partyLock = GetPartyLock(partyId);
        lock (partyLock)
        {
            PersistStateInternal(party);
        }
    }

    // --- Private ---

    private Lock GetPartyLock(string partyId)
    {
        return _partyLocks.GetOrAdd(partyId, _ => new Lock());
    }

    private void MapSession(string sessionId, string partyId)
    {
        // A session can only be in one party at a time
        _sessionToPartyId[sessionId] = partyId;
    }

    private void PersistStateInternal(Party party)
    {
        try
        {
            var json = JsonSerializer.Serialize(party, JsonOptions);
            var filePath = Path.Combine(_partiesDir, $"{party.Id}.json");
            var tmpPath = filePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist party state for {PartyId}", party.Id);
        }
    }

    private void LoadAllParties()
    {
        var files = Directory.GetFiles(_partiesDir, "*.json");
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var party = JsonSerializer.Deserialize<Party>(json, JsonOptions);
                if (party == null) continue;

                _parties[party.Id] = party;

                // Rebuild session-to-party mappings
                if (!string.IsNullOrEmpty(party.HostSessionId))
                    _sessionToPartyId[party.HostSessionId] = party.Id;
                foreach (var guestId in party.Guests.Keys)
                    _sessionToPartyId[guestId] = party.Id;

                _logger.LogInformation("Loaded party {PartyId} (invite: {Code}, queue: {Count} items)",
                    party.Id, party.InviteCode, party.Queue.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load party state from {Path}", file);
            }
        }

        if (_parties.Count > 0)
            _logger.LogInformation("Loaded {Count} party/parties from disk", _parties.Count);
    }

    private void MigrateLegacyState(string dataDir)
    {
        var legacyPath = Path.Combine(dataDir, "party-state.json");
        if (!File.Exists(legacyPath)) return;

        try
        {
            var json = File.ReadAllText(legacyPath);
            if (json == "null" || string.IsNullOrWhiteSpace(json))
            {
                File.Move(legacyPath, legacyPath + ".migrated", overwrite: true);
                return;
            }

            var party = JsonSerializer.Deserialize<Party>(json, JsonOptions);
            if (party != null)
            {
                var newPath = Path.Combine(_partiesDir, $"{party.Id}.json");
                File.WriteAllText(newPath, json);
                _logger.LogInformation("Migrated legacy party-state.json to {Path}", newPath);
            }
            File.Move(legacyPath, legacyPath + ".migrated", overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate legacy party state from {Path}", legacyPath);
        }
    }
}
