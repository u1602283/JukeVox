using FluentAssertions;
using JukeVox.Server.Models;
using JukeVox.Server.Services;
using JukeVox.Server.Tests.Helpers;
using Moq;
using NUnit.Framework;

namespace JukeVox.Server.Tests.Services;

[TestFixture]
public class QueueServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _party = TestData.CreateParty();
        _party.Id = PartyId;
        _partyServiceMock = new Mock<IPartyService>();
        _partyServiceMock.Setup(p => p.GetParty(PartyId)).Returns(_party).Verifiable(Times.Once);
        _service = new QueueService(_partyServiceMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _partyServiceMock.VerifyAll();
        _partyServiceMock.VerifyNoOtherCalls();
    }

    private const string PartyId = "test1234";
    private Party _party = null!;
    private Mock<IPartyService> _partyServiceMock = null!;
    private QueueService _service = null!;

    [Test]
    public void AddToQueue_AsHost_AddsItem()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var request = TestData.CreateAddToQueueRequest("Host Song");

        var (item, error) = _service.AddToQueue(PartyId, _party.HostSessionId, request);

        item.Should().NotBeNull();
        error.Should().BeNull();
        item.TrackName.Should().Be("Host Song");
        item.AddedByName.Should().Be("Host");
    }

    [Test]
    public void AddToQueue_AsGuest_SpendsCreditAndUsesDisplayName()
    {
        _partyServiceMock.Setup(p => p.TrySpendCredit(PartyId, "guest-1"))
            .Returns(("Alice", null))
            .Verifiable(Times.Once);
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var request = TestData.CreateAddToQueueRequest("Guest Song");

        var (item, error) = _service.AddToQueue(PartyId, "guest-1", request);

        item.Should().NotBeNull();
        error.Should().BeNull();
        item.AddedByName.Should().Be("Alice");
    }

    [Test]
    public void AddToQueue_GuestWithZeroCredits_ReturnsError()
    {
        _partyServiceMock.Setup(p => p.TrySpendCredit(PartyId, "guest-1"))
            .Returns((null, "No credits remaining"))
            .Verifiable(Times.Once);

        var (item, error) = _service.AddToQueue(PartyId, "guest-1", TestData.CreateAddToQueueRequest());

        item.Should().BeNull();
        error.Should().Be("No credits remaining");
    }

    [Test]
    public void AddToQueue_NonParticipant_ReturnsError()
    {
        _partyServiceMock.Setup(p => p.TrySpendCredit(PartyId, "stranger"))
            .Returns((null, "Not a party participant"))
            .Verifiable(Times.Once);

        var (item, error) = _service.AddToQueue(PartyId, "stranger", TestData.CreateAddToQueueRequest());

        item.Should().BeNull();
        error.Should().Be("Not a party participant");
    }

    [Test]
    public void AddToQueue_InsertBeforeBasePlaylistItems()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        // Add a base playlist item first
        _party.Queue.Add(TestData.CreateQueueItem("Base Song", true));

        var request = TestData.CreateAddToQueueRequest("User Song");
        _service.AddToQueue(PartyId, _party.HostSessionId, request);

        _party.Queue[0].TrackName.Should().Be("User Song");
        _party.Queue[1].TrackName.Should().Be("Base Song");
    }

    [Test]
    public void RemoveFromQueue_ExistingItem_ReturnsTrue()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var item = TestData.CreateQueueItem("Song");
        _party.Queue.Add(item);

        _service.RemoveFromQueue(PartyId, item.Id).Should().BeTrue();
        _party.Queue.Should().BeEmpty();
    }

    [Test]
    public void RemoveFromQueue_NotFound_ReturnsFalse()
        => _service.RemoveFromQueue(PartyId, "nonexistent").Should().BeFalse();

    [Test]
    public void Reorder_CorrectOrder()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var item1 = TestData.CreateQueueItem("Song 1");
        var item2 = TestData.CreateQueueItem("Song 2");
        var item3 = TestData.CreateQueueItem("Song 3");
        _party.Queue.AddRange([item1, item2, item3]);

        _service.Reorder(PartyId, [item3.Id, item1.Id, item2.Id]);

        _party.Queue.Select(q => q.TrackName).Should().Equal("Song 3", "Song 1", "Song 2");
    }

    [Test]
    public void Reorder_MissingItemsAppended()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var item1 = TestData.CreateQueueItem("Song 1");
        var item2 = TestData.CreateQueueItem("Song 2");
        _party.Queue.AddRange([item1, item2]);

        // Only reorder item2, item1 should be appended
        _service.Reorder(PartyId, [item2.Id]);

        _party.Queue.Select(q => q.TrackName).Should().Equal("Song 2", "Song 1");
    }

    [Test]
    public void Dequeue_ReturnsFirstItem()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var item1 = TestData.CreateQueueItem("Song 1");
        var item2 = TestData.CreateQueueItem("Song 2");
        _party.Queue.AddRange([item1, item2]);

        var result = _service.Dequeue(PartyId);

        result.Should().NotBeNull();
        result.TrackName.Should().Be("Song 1");
        _party.Queue.Should().ContainSingle();
    }

    [Test]
    public void Dequeue_UpdatesCurrentTrackAndHistory()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var currentTrack = TestData.CreateQueueItem("Current");
        _party.CurrentTrack = currentTrack;

        var nextItem = TestData.CreateQueueItem("Next");
        _party.Queue.Add(nextItem);

        _service.Dequeue(PartyId);

        _party.CurrentTrack!.TrackName.Should().Be("Next");
        _party.PlaybackHistory.Should()
            .ContainSingle()
            .Which.TrackName.Should()
            .Be("Current");
    }

    [Test]
    public void Dequeue_EmptyQueue_ReturnsNull() => _service.Dequeue(PartyId).Should().BeNull();

    [Test]
    public void Dequeue_AutoRefillsFromBasePlaylist()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        _party.BasePlaylistTracks.Add(TestData.CreateBasePlaylistTrack("Base 1"));
        _party.BasePlaylistTracks.Add(TestData.CreateBasePlaylistTrack("Base 2"));

        var item = TestData.CreateQueueItem("Last Song");
        _party.Queue.Add(item);

        _service.Dequeue(PartyId);

        // Queue should have been refilled from base playlist
        _party.Queue.Should().HaveCount(2);
        _party.Queue.Should().OnlyContain(q => q.IsFromBasePlaylist);
    }

    [Test]
    public void SkipToPrevious_PopsHistoryAndRequeuesCurrent()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var prevTrack = TestData.CreateQueueItem("Previous");
        var currentTrack = TestData.CreateQueueItem("Current");
        _party.PlaybackHistory.Add(prevTrack);
        _party.CurrentTrack = currentTrack;

        var result = _service.SkipToPrevious(PartyId);

        result.Should().NotBeNull();
        result.TrackName.Should().Be("Previous");
        _party.CurrentTrack!.TrackName.Should().Be("Previous");
        _party.PlaybackHistory.Should().BeEmpty();
        _party.Queue.Should()
            .ContainSingle()
            .Which.TrackName.Should()
            .Be("Current");
    }

    [Test]
    public void SkipToPrevious_EmptyHistory_ReturnsNull()
    {
        _party.CurrentTrack = TestData.CreateQueueItem("Current");

        _service.SkipToPrevious(PartyId).Should().BeNull();
    }

    [Test]
    public void SkipToPrevious_MultiplePops()
    {
        _partyServiceMock.Setup(p => p.GetParty(PartyId)).Returns(_party).Verifiable(Times.Exactly(3));
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Exactly(2));
        var track1 = TestData.CreateQueueItem("Track 1");
        var track2 = TestData.CreateQueueItem("Track 2");
        var current = TestData.CreateQueueItem("Current");
        _party.PlaybackHistory.AddRange([track1, track2]);
        _party.CurrentTrack = current;

        // First skip back
        _service.SkipToPrevious(PartyId)!.TrackName.Should().Be("Track 2");

        // Second skip back
        _service.SkipToPrevious(PartyId)!.TrackName.Should().Be("Track 1");

        // Third skip back — no more history
        _service.SkipToPrevious(PartyId).Should().BeNull();
    }

    [Test]
    public void GetQueue_ReturnsDtos()
    {
        _party.Queue.Add(TestData.CreateQueueItem("Song 1"));
        _party.Queue.Add(TestData.CreateQueueItem("Song 2"));

        var queue = _service.GetQueue(PartyId);

        queue.Should().HaveCount(2);
        queue[0].TrackName.Should().Be("Song 1");
        queue[1].TrackName.Should().Be("Song 2");
    }

    [Test]
    public void GetQueue_ReturnsDtosWithScore()
    {
        var item = TestData.CreateQueueItem("Song");
        item.Votes["voter-1"] = 1;
        _party.Queue.Add(item);

        var queue = _service.GetQueue(PartyId);

        queue[0].Score.Should().Be(1);
    }

    [Test]
    public void Vote_Upvote_IncrementsScore()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var item = TestData.CreateQueueItem("Song");
        _party.Queue.Add(item);

        var (success, error) = _service.Vote(PartyId, _party.HostSessionId, item.Id, 1);

        success.Should().BeTrue();
        error.Should().BeNull();
        item.Score.Should().Be(1);
    }

    [Test]
    public void Vote_Downvote_DecrementsScore()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var item = TestData.CreateQueueItem("Song");
        _party.Queue.Add(item);

        var (success, _) = _service.Vote(PartyId, _party.HostSessionId, item.Id, -1);

        success.Should().BeTrue();
        item.Score.Should().Be(-1);
    }

    [Test]
    public void Vote_RemoveVote_ClearsEntry()
    {
        _partyServiceMock.Setup(p => p.GetParty(PartyId)).Returns(_party).Verifiable(Times.Exactly(2));
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Exactly(2));
        var item = TestData.CreateQueueItem("Song");
        _party.Queue.Add(item);

        _service.Vote(PartyId, _party.HostSessionId, item.Id, 1);
        _service.Vote(PartyId, _party.HostSessionId, item.Id, 0);

        item.Score.Should().Be(0);
        item.Votes.Should().NotContainKey(_party.HostSessionId);
    }

    [Test]
    public void Vote_InvalidValue_ReturnsError()
    {
        var item = TestData.CreateQueueItem("Song");
        _party.Queue.Add(item);

        var (success, error) = _service.Vote(PartyId, _party.HostSessionId, item.Id, 5);

        success.Should().BeFalse();
        error.Should().Be("Vote must be -1, 0, or 1");
    }

    [Test]
    public void Vote_BasePlaylistItem_Succeeds()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var item = TestData.CreateQueueItem("Base Song", true);
        _party.Queue.Add(item);

        var (success, error) = _service.Vote(PartyId, _party.HostSessionId, item.Id, 1);

        success.Should().BeTrue();
        error.Should().BeNull();
        item.Score.Should().Be(1);
    }

    [Test]
    public void Vote_NonParticipant_ReturnsError()
    {
        var item = TestData.CreateQueueItem("Song");
        _party.Queue.Add(item);

        var (success, error) = _service.Vote(PartyId, "stranger", item.Id, 1);

        success.Should().BeFalse();
        error.Should().Be("Not a party participant");
    }

    [Test]
    public void Vote_ItemNotFound_ReturnsError()
    {
        var (success, error) = _service.Vote(PartyId, _party.HostSessionId, "nonexistent", 1);

        success.Should().BeFalse();
        error.Should().Be("Item not found");
    }

    [Test]
    public void Vote_AutoRemovesAtNegativeThree()
    {
        _partyServiceMock.Setup(p => p.GetParty(PartyId)).Returns(_party).Verifiable(Times.Exactly(3));
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Exactly(3));
        var item = TestData.CreateQueueItem("Hated Song");
        _party.Queue.Add(item);

        var guest1 = TestData.CreateGuestSession("g1", "G1");
        var guest2 = TestData.CreateGuestSession("g2", "G2");
        var guest3 = TestData.CreateGuestSession("g3", "G3");
        _party.Guests["g1"] = guest1;
        _party.Guests["g2"] = guest2;
        _party.Guests["g3"] = guest3;

        _service.Vote(PartyId, "g1", item.Id, -1);
        _service.Vote(PartyId, "g2", item.Id, -1);
        _service.Vote(PartyId, "g3", item.Id, -1);

        _party.Queue.Should().BeEmpty();
    }

    [Test]
    public void Vote_BelowThreshold_DoesNotChangeOrder()
    {
        _partyServiceMock.Setup(p => p.GetParty(PartyId)).Returns(_party).Verifiable(Times.Exactly(2));
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Exactly(2));

        var item1 = TestData.CreateQueueItem("Song 1");
        item1.InsertionOrder = 0;
        var item2 = TestData.CreateQueueItem("Song 2");
        item2.InsertionOrder = 1;
        var item3 = TestData.CreateQueueItem("Song 3");
        item3.InsertionOrder = 2;
        _party.Queue.AddRange([item1, item2, item3]);

        var guest = TestData.CreateGuestSession("g1", "G1");
        _party.Guests["g1"] = guest;

        // Two upvotes on Song 3 (below threshold of 3) — order should not change
        _service.Vote(PartyId, _party.HostSessionId, item3.Id, 1);
        _service.Vote(PartyId, "g1", item3.Id, 1);

        _party.Queue[0].TrackName.Should().Be("Song 1");
        _party.Queue[1].TrackName.Should().Be("Song 2");
        _party.Queue[2].TrackName.Should().Be("Song 3");
    }

    [Test]
    public void Vote_ThreeUpvotes_PromotesToTop()
    {
        _partyServiceMock.Setup(p => p.GetParty(PartyId)).Returns(_party).Verifiable(Times.Exactly(3));
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Exactly(3));

        var item1 = TestData.CreateQueueItem("Song 1");
        item1.InsertionOrder = 0;
        var item2 = TestData.CreateQueueItem("Song 2");
        item2.InsertionOrder = 1;
        var item3 = TestData.CreateQueueItem("Song 3");
        item3.InsertionOrder = 2;
        _party.Queue.AddRange([item1, item2, item3]);

        var g1 = TestData.CreateGuestSession("g1", "G1");
        var g2 = TestData.CreateGuestSession("g2", "G2");
        _party.Guests["g1"] = g1;
        _party.Guests["g2"] = g2;

        // Three upvotes on Song 3 — should promote to top
        _service.Vote(PartyId, _party.HostSessionId, item3.Id, 1);
        _service.Vote(PartyId, "g1", item3.Id, 1);
        _service.Vote(PartyId, "g2", item3.Id, 1);

        _party.Queue[0].TrackName.Should().Be("Song 3");
        _party.Queue[1].TrackName.Should().Be("Song 1");
        _party.Queue[2].TrackName.Should().Be("Song 2");
    }

    [Test]
    public void Vote_DownvoteDoesNotMoveItemDown()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var item1 = TestData.CreateQueueItem("Song 1");
        item1.InsertionOrder = 0;
        var item2 = TestData.CreateQueueItem("Song 2");
        item2.InsertionOrder = 1;
        var item3 = TestData.CreateQueueItem("Song 3");
        item3.InsertionOrder = 2;
        _party.Queue.AddRange([item1, item2, item3]);

        // Downvote Song 1 — it should stay in place
        _service.Vote(PartyId, _party.HostSessionId, item1.Id, -1);

        _party.Queue[0].TrackName.Should().Be("Song 1");
        _party.Queue[1].TrackName.Should().Be("Song 2");
        _party.Queue[2].TrackName.Should().Be("Song 3");
    }

    [Test]
    public void GetUserVotes_ReturnsVotesForSession()
    {
        _partyServiceMock.Setup(p => p.GetParty(PartyId)).Returns(_party).Verifiable(Times.Exactly(2));
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var item = TestData.CreateQueueItem("Song");
        _party.Queue.Add(item);

        _service.Vote(PartyId, _party.HostSessionId, item.Id, 1);

        var votes = _service.GetUserVotes(PartyId, _party.HostSessionId);
        votes.Should().ContainKey(item.Id);
        votes[item.Id].Should().Be(1);
    }

    [Test]
    public void GetUserVotes_EmptyWhenNoVotes()
    {
        _party.Queue.Add(TestData.CreateQueueItem("Song"));

        var votes = _service.GetUserVotes(PartyId, "guest-1");
        votes.Should().BeEmpty();
    }

    [Test]
    public void AddToQueue_AssignsInsertionOrder()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var request = TestData.CreateAddToQueueRequest("Song");

        var (item, _) = _service.AddToQueue(PartyId, _party.HostSessionId, request);

        item!.InsertionOrder.Should().Be(0);
        _party.NextInsertionOrder.Should().Be(1);
    }

    [Test]
    public void AddToQueue_IncrementsInsertionOrder()
    {
        _partyServiceMock.Setup(p => p.GetParty(PartyId)).Returns(_party).Verifiable(Times.Exactly(2));
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Exactly(2));

        var (item1, _) = _service.AddToQueue(PartyId, _party.HostSessionId, TestData.CreateAddToQueueRequest("Song 1"));
        var (item2, _) = _service.AddToQueue(PartyId, _party.HostSessionId, TestData.CreateAddToQueueRequest("Song 2"));

        item1!.InsertionOrder.Should().Be(0);
        item2!.InsertionOrder.Should().Be(1);
    }

    [Test]
    public void Reorder_ReassignsInsertionOrders()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var item1 = TestData.CreateQueueItem("Song 1");
        item1.InsertionOrder = 0;
        var item2 = TestData.CreateQueueItem("Song 2");
        item2.InsertionOrder = 1;
        _party.Queue.AddRange([item1, item2]);

        _service.Reorder(PartyId, [item2.Id, item1.Id]);

        _party.Queue[0].TrackName.Should().Be("Song 2");
        _party.Queue[0].InsertionOrder.Should().Be(0);
        _party.Queue[1].TrackName.Should().Be("Song 1");
        _party.Queue[1].InsertionOrder.Should().Be(1);
        _party.NextInsertionOrder.Should().Be(2);
    }

    [Test]
    public void Vote_SingleUpvote_DoesNotResortQueue()
    {
        // Simulates legacy state where InsertionOrders don't match physical order
        // (e.g. from old per-vote sorting). A sub-threshold vote should never re-sort.
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);

        var item1 = TestData.CreateQueueItem("Song A");
        item1.InsertionOrder = 2; // Out of order!
        var item2 = TestData.CreateQueueItem("Song B");
        item2.InsertionOrder = 0;
        var item3 = TestData.CreateQueueItem("Song C");
        item3.InsertionOrder = 1;
        _party.Queue.AddRange([item1, item2, item3]);

        // Upvote Song B — should NOT trigger a re-sort
        _service.Vote(PartyId, _party.HostSessionId, item2.Id, 1);

        _party.Queue[0].TrackName.Should().Be("Song A");
        _party.Queue[1].TrackName.Should().Be("Song B");
        _party.Queue[2].TrackName.Should().Be("Song C");
        item2.Score.Should().Be(1);
    }

    [Test]
    public void Vote_AsGuest_Succeeds()
    {
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Once);
        var guest = TestData.CreateGuestSession("guest-1", "Alice");
        _party.Guests["guest-1"] = guest;
        var item = TestData.CreateQueueItem("Song");
        _party.Queue.Add(item);

        var (success, error) = _service.Vote(PartyId, "guest-1", item.Id, 1);

        success.Should().BeTrue();
        error.Should().BeNull();
        item.Score.Should().Be(1);
    }

    [Test]
    public void Vote_SingleUpvote_WithBasePlaylist_DoesNotChangeOrder()
    {
        // Mimics real app: base playlist set, then manual songs added, then upvote
        // SetBasePlaylist(1) + AddToQueue(3) + GetQueue(2) + Vote(1) = 7
        _partyServiceMock.Setup(p => p.GetParty(PartyId)).Returns(_party).Verifiable(Times.Exactly(7));
        // SetBasePlaylist(1) + AddToQueue(3) + Vote(1) = 5
        _partyServiceMock.Setup(p => p.PersistState(PartyId)).Verifiable(Times.Exactly(5));
        // 3 guest AddToQueue calls
        _partyServiceMock.Setup(p => p.TrySpendCredit(PartyId, "guest-1"))
            .Returns(("Alice", null))
            .Verifiable(Times.Exactly(3));

        // Set up base playlist (adds shuffled base items to queue)
        var baseTracks = new List<BasePlaylistTrack>
        {
            TestData.CreateBasePlaylistTrack("Base 1"),
            TestData.CreateBasePlaylistTrack("Base 2"),
            TestData.CreateBasePlaylistTrack("Base 3")
        };
        _service.SetBasePlaylist(PartyId, baseTracks, "playlist-123", "Test Playlist");

        // Guest adds manual songs (these should go before base playlist items)
        var guest = TestData.CreateGuestSession("guest-1", "Alice");
        _party.Guests["guest-1"] = guest;
        _service.AddToQueue(PartyId, "guest-1", TestData.CreateAddToQueueRequest("Manual 1"));
        _service.AddToQueue(PartyId, "guest-1", TestData.CreateAddToQueueRequest("Manual 2"));
        _service.AddToQueue(PartyId, "guest-1", TestData.CreateAddToQueueRequest("Manual 3"));

        // Verify initial order: manual songs before base playlist
        var queueBefore = _service.GetQueue(PartyId);
        queueBefore[0].TrackName.Should().Be("Manual 1");
        queueBefore[1].TrackName.Should().Be("Manual 2");
        queueBefore[2].TrackName.Should().Be("Manual 3");
        // Base items are after (indices 3, 4, 5)
        queueBefore.Skip(3).Should().AllSatisfy(q => q.IsFromBasePlaylist.Should().BeTrue());

        // Now upvote Manual 2 — this should NOT change the order
        _service.Vote(PartyId, "guest-1", queueBefore[1].Id, 1);

        var queueAfter = _service.GetQueue(PartyId);
        queueAfter[0].TrackName.Should().Be("Manual 1");
        queueAfter[1].TrackName.Should().Be("Manual 2");
        queueAfter[1].Score.Should().Be(1);
        queueAfter[2].TrackName.Should().Be("Manual 3");
    }
}
