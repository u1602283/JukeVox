using System.Collections.Concurrent;

namespace JukeVox.Server.Services;

public class ConnectionMapping
{
    private readonly ConcurrentDictionary<string, string> _connectionToSession = new();
    private readonly ConcurrentDictionary<string, string> _sessionToConnection = new();

    public void Add(string sessionId, string connectionId)
    {
        if (_sessionToConnection.TryGetValue(sessionId, out var old))
        {
            _connectionToSession.TryRemove(old, out _);
        }

        _sessionToConnection[sessionId] = connectionId;
        _connectionToSession[connectionId] = sessionId;
    }

    public void RemoveByConnection(string connectionId)
    {
        if (_connectionToSession.TryRemove(connectionId, out var sessionId))
        {
            _sessionToConnection.TryRemove(sessionId, out _);
        }
    }

    public string? GetConnectionId(string sessionId)
    {
        _sessionToConnection.TryGetValue(sessionId, out var connectionId);
        return connectionId;
    }
}
