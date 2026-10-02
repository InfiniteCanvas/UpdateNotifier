using UpdateNotifier.Services;

namespace UpdateNotifier.Tests;

public sealed class ThumbnailQueueTests
{
	private static readonly DateTime OlderUpdate = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime NewerUpdate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void TryDequeue_MixedTiers_WatchedGamesFirst()
	{
		var queue = new ThumbnailQueue();
		queue.EnqueueHousekeeping([(100, NewerUpdate)]);
		queue.EnqueueWatched([(200, OlderUpdate)]);

		Assert.True(queue.TryDequeue(out var watched));
		Assert.Equal(200ul, watched);
		Assert.True(queue.TryDequeue(out var housekeeping));
		Assert.Equal(100ul, housekeeping);
		Assert.False(queue.TryDequeue(out _));
	}

	[Fact]
	public void TryDequeue_WithinTier_NewestLastUpdatedFirst()
	{
		var queue = new ThumbnailQueue();
		queue.EnqueueWatched([(300, OlderUpdate)]); // older game enqueued first...
		queue.EnqueueWatched([(400, NewerUpdate)]); // ...but the newer update dequeues first

		Assert.True(queue.TryDequeue(out var first));
		Assert.Equal(400ul, first);
		Assert.True(queue.TryDequeue(out var second));
		Assert.Equal(300ul, second);
		Assert.False(queue.TryDequeue(out _));
	}

	[Fact]
	public void EnqueueWatched_PromotedFromHousekeeping_JumpsAheadAndDequeuesOnce()
	{
		var queue = new ThumbnailQueue();
		queue.EnqueueWatched([(500, OlderUpdate)]); // an older game already in the watched tier
		queue.EnqueueHousekeeping([(600, NewerUpdate)]);
		queue.EnqueueWatched([(600, NewerUpdate)]); // a watchlist view promotes it

		Assert.True(queue.TryDequeue(out var first));
		Assert.Equal(600ul, first); // promoted ahead of the older watched game
		Assert.True(queue.TryDequeue(out var second));
		Assert.Equal(500ul, second);
		Assert.False(queue.TryDequeue(out _)); // the stale housekeeping entry never dequeues twice
	}

	[Fact]
	public void EnqueueWatched_Duplicate_DedupsToSingleDequeue()
	{
		var queue = new ThumbnailQueue();
		queue.EnqueueWatched([(700, NewerUpdate)]);
		queue.EnqueueWatched([(700, NewerUpdate)]);

		Assert.True(queue.TryDequeue(out var gameId));
		Assert.Equal(700ul, gameId);
		Assert.False(queue.TryDequeue(out _));
	}

	[Fact]
	public void MarkFailed_AfterThreeStrikes_RefusesReenqueue()
	{
		var queue = new ThumbnailQueue();
		const ulong gameId = 800;

		queue.MarkFailed(gameId);
		queue.MarkFailed(gameId);
		queue.MarkFailed(gameId);
		queue.EnqueueWatched([(gameId, NewerUpdate)]);

		Assert.False(queue.Contains(gameId));
		Assert.False(queue.TryDequeue(out _));
	}

	[Fact]
	public void Contains_PendingGame_TrueUntilDequeued()
	{
		var queue = new ThumbnailQueue();
		queue.EnqueueWatched([(900, NewerUpdate)]);

		Assert.True(queue.Contains(900));

		Assert.True(queue.TryDequeue(out var gameId));
		Assert.Equal(900ul, gameId);
		Assert.False(queue.Contains(900));
	}
}
