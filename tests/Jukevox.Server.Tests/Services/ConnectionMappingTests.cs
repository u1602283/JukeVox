using FluentAssertions;
using NUnit.Framework;
using JukeVox.Server.Services;

namespace JukeVox.Server.Tests.Services;

[TestFixture]
public class ConnectionMappingTests
{
    private ConnectionMapping _mapping = null!;

    [SetUp]
    public void SetUp() => _mapping = new ConnectionMapping();

    [Test]
    public void Add_And_GetConnectionId_ReturnsStoredValue()
    {
        _mapping.Add("session-1", "conn-1");

        _mapping.GetConnectionId("session-1").Should().Be("conn-1");
    }

    [Test]
    public void GetConnectionId_UnknownSession_ReturnsNull()
    {
        _mapping.GetConnectionId("unknown").Should().BeNull();
    }

    [Test]
    public void Add_SameSessionTwice_OverwritesPrevious()
    {
        _mapping.Add("session-1", "conn-1");
        _mapping.Add("session-1", "conn-2");

        _mapping.GetConnectionId("session-1").Should().Be("conn-2");
    }

    [Test]
    public void RemoveByConnection_RemovesBothDirections()
    {
        _mapping.Add("session-1", "conn-1");

        _mapping.RemoveByConnection("conn-1");

        _mapping.GetConnectionId("session-1").Should().BeNull();
    }

    [Test]
    public void RemoveByConnection_UnknownConnection_DoesNotThrow()
    {
        var act = () => _mapping.RemoveByConnection("unknown");

        act.Should().NotThrow();
    }

    [Test]
    public void RemoveByConnection_OnlyRemovesTargetPair()
    {
        _mapping.Add("session-1", "conn-1");
        _mapping.Add("session-2", "conn-2");

        _mapping.RemoveByConnection("conn-1");

        _mapping.GetConnectionId("session-1").Should().BeNull();
        _mapping.GetConnectionId("session-2").Should().Be("conn-2");
    }
}
