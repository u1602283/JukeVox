using FluentAssertions;
using Moq;
using NUnit.Framework;
using JukeVox.Server.Models;
using JukeVox.Server.Models.Dto;
using JukeVox.Server.Services;
using JukeVox.Server.Tests.Helpers;

namespace JukeVox.Server.Tests.Services;

[TestFixture]
public class QueueServiceTests
{
    private Party _party = null!;
    private Mock<IPartyService> _partyServiceMock = null!;
    private QueueService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _party = TestData.CreateParty();
        _partyServiceMock = new Mock<IPartyService>();
        _partyServiceMock.Setup(p => p.GetCurrentParty()).Returns(_party).Verifiable(Times.Once);
        _service = new QueueService(_partyServiceMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _partyServiceMock.VerifyAll();
        _partyServiceMock.VerifyNoOtherCalls();
    }

    [Test]
    public void AddToQueue_AsHost_AddsItem()
    {
        _partyServiceMock.Setup(p => p.PersistState()).Verifiable(Times.Once);
        var request = TestData.CreateAddToQueueRequest("Host Song");

        var (item, error) = _service.AddToQueue(_party.HostSessionId, request);

        item.Should().NotBeNull();
        error.Should().BeNull();
        item!.TrackName.Should().Be("Host Song");
        item.AddedByName.Should().Be("Host");
    }

    [Test]
    public void AddToQueue_AsGuest_DecrementsCredits()
    {
        _partyServiceMock.Setup(p => p.PersistState()).Verifiable(Times.Once);
        var guest = TestData.CreateGuestSession("guest-1", "Alice", 3);
        _party.Guests["guest-1"] = guest;
        var request = TestData.CreateAddToQueueRequest("Guest Song");

        var (item, error) = _service.AddToQueue("guest-1", request);

        item.Should().NotBeNull();
        error.Should().BeNull();
        item!.AddedByName.Should().Be("Alice");
        guest.CreditsRemaining.Should().Be(2);
    }

    [Test]
    public void AddToQueue_GuestWithZeroCredits_ReturnsError()
    {
        var guest = TestData.CreateGuestSession("guest-1", "Alice", 0);
        _party.Guests["guest-1"] = guest;

        var (item, error) = _service.AddToQueue("guest-1", TestData.CreateAddToQueueRequest());

        item.Should().BeNull();
        error.Should().Be("No credits remaining");
    }

    [Test]
    public void AddToQueue_NonParticipant_ReturnsError()
    {
        var (item, error) = _service.AddToQueue("stranger", TestData.CreateAddToQueueRequest());

        item.Should().BeNull();
        error.Should().Be("Not a party participant");
    }

    [Test]
    public void AddToQueue_InsertBeforeBasePlaylistItems()
    {
        _partyServiceMock.Setup(p => p.PersistState()).Verifiable(Times.Once);
        // Add a base playlist item first
        _party.Queue.Add(TestData.CreateQueueItem("Base Song", isFromBasePlaylist: true));

        var request = TestData.CreateAddToQueueRequest("User Song");
        _service.AddToQueue(_party.HostSessionId, request);

        _party.Queue[0].TrackName.Should().Be("User Song");
        _party.Queue[1].TrackName.Should().Be("Base Song");
    }

    [Test]
    public void RemoveFromQueue_ExistingItem_ReturnsTrue()
    {
        _partyServiceMock.Setup(p => p.PersistState()).Verifiable(Times.Once);
        var item = TestData.CreateQueueItem("Song");
        _party.Queue.Add(item);

        _service.RemoveFromQueue(item.Id).Should().BeTrue();
        _party.Queue.Should().BeEmpty();
    }

    [Test]
    public void RemoveFromQueue_NotFound_ReturnsFalse()
    {
        _service.RemoveFromQueue("nonexistent").Should().BeFalse();
    }

    [Test]
    public void Reorder_CorrectOrder()
    {
        _partyServiceMock.Setup(p => p.PersistState()).Verifiable(Times.Once);
        var item1 = TestData.CreateQueueItem("Song 1");
        var item2 = TestData.CreateQueueItem("Song 2");
        var item3 = TestData.CreateQueueItem("Song 3");
        _party.Queue.AddRange([item1, item2, item3]);

        _service.Reorder([item3.Id, item1.Id, item2.Id]);

        _party.Queue.Select(q => q.TrackName).Should().Equal("Song 3", "Song 1", "Song 2");
    }

    [Test]
    public void Reorder_MissingItemsAppended()
    {
        _partyServiceMock.Setup(p => p.PersistState()).Verifiable(Times.Once);
        var item1 = TestData.CreateQueueItem("Song 1");
        var item2 = TestData.CreateQueueItem("Song 2");
        _party.Queue.AddRange([item1, item2]);

        // Only reorder item2, item1 should be appended
        _service.Reorder([item2.Id]);

        _party.Queue.Select(q => q.TrackName).Should().Equal("Song 2", "Song 1");
    }

    [Test]
    public void Dequeue_ReturnsFirstItem()
    {
        _partyServiceMock.Setup(p => p.PersistState()).Verifiable(Times.Once);
        var item1 = TestData.CreateQueueItem("Song 1");
        var item2 = TestData.CreateQueueItem("Song 2");
        _party.Queue.AddRange([item1, item2]);

        var result = _service.Dequeue();

        result.Should().NotBeNull();
        result!.TrackName.Should().Be("Song 1");
        _party.Queue.Should().ContainSingle();
    }

    [Test]
    public void Dequeue_UpdatesCurrentTrackAndHistory()
    {
        _partyServiceMock.Setup(p => p.PersistState()).Verifiable(Times.Once);
        var currentTrack = TestData.CreateQueueItem("Current");
        _party.CurrentTrack = currentTrack;

        var nextItem = TestData.CreateQueueItem("Next");
        _party.Queue.Add(nextItem);

        _service.Dequeue();

        _party.CurrentTrack!.TrackName.Should().Be("Next");
        _party.PlaybackHistory.Should().ContainSingle()
            .Which.TrackName.Should().Be("Current");
    }

    [Test]
    public void Dequeue_EmptyQueue_ReturnsNull()
    {
        _service.Dequeue().Should().BeNull();
    }

    [Test]
    public void Dequeue_AutoRefillsFromBasePlaylist()
    {
        _partyServiceMock.Setup(p => p.PersistState()).Verifiable(Times.Once);
        _party.BasePlaylistTracks.Add(TestData.CreateBasePlaylistTrack("Base 1"));
        _party.BasePlaylistTracks.Add(TestData.CreateBasePlaylistTrack("Base 2"));

        var item = TestData.CreateQueueItem("Last Song");
        _party.Queue.Add(item);

        _service.Dequeue();

        // Queue should have been refilled from base playlist
        _party.Queue.Should().HaveCount(2);
        _party.Queue.Should().OnlyContain(q => q.IsFromBasePlaylist);
    }

    [Test]
    public void SkipToPrevious_PopsHistoryAndRequeuesCurrent()
    {
        _partyServiceMock.Setup(p => p.PersistState()).Verifiable(Times.Once);
        var prevTrack = TestData.CreateQueueItem("Previous");
        var currentTrack = TestData.CreateQueueItem("Current");
        _party.PlaybackHistory.Add(prevTrack);
        _party.CurrentTrack = currentTrack;

        var result = _service.SkipToPrevious();

        result.Should().NotBeNull();
        result!.TrackName.Should().Be("Previous");
        _party.CurrentTrack!.TrackName.Should().Be("Previous");
        _party.PlaybackHistory.Should().BeEmpty();
        _party.Queue.Should().ContainSingle()
            .Which.TrackName.Should().Be("Current");
    }

    [Test]
    public void SkipToPrevious_EmptyHistory_ReturnsNull()
    {
        _party.CurrentTrack = TestData.CreateQueueItem("Current");

        _service.SkipToPrevious().Should().BeNull();
    }

    [Test]
    public void SkipToPrevious_MultiplePops()
    {
        _partyServiceMock.Setup(p => p.GetCurrentParty()).Returns(_party).Verifiable(Times.Exactly(3));
        _partyServiceMock.Setup(p => p.PersistState()).Verifiable(Times.Exactly(2));
        var track1 = TestData.CreateQueueItem("Track 1");
        var track2 = TestData.CreateQueueItem("Track 2");
        var current = TestData.CreateQueueItem("Current");
        _party.PlaybackHistory.AddRange([track1, track2]);
        _party.CurrentTrack = current;

        // First skip back
        _service.SkipToPrevious()!.TrackName.Should().Be("Track 2");

        // Second skip back
        _service.SkipToPrevious()!.TrackName.Should().Be("Track 1");

        // Third skip back — no more history
        _service.SkipToPrevious().Should().BeNull();
    }

    [Test]
    public void GetQueue_ReturnsDtos()
    {
        _party.Queue.Add(TestData.CreateQueueItem("Song 1"));
        _party.Queue.Add(TestData.CreateQueueItem("Song 2"));

        var queue = _service.GetQueue();

        queue.Should().HaveCount(2);
        queue[0].TrackName.Should().Be("Song 1");
        queue[1].TrackName.Should().Be("Song 2");
    }
}
