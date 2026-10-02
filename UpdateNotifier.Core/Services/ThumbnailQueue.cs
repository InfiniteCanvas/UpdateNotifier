namespace UpdateNotifier.Services;

/// <summary>
///     In-memory work queue for the thumbnail scraper. Watched games jump the line (a user is looking
///     at them right now); housekeeping fills the rest. Within a tier the newest update dequeues first.
///     Deliberately not persisted: a restart only loses pending work, and the scraper's startup sweep
///     re-enqueues every game still missing a thumbnail within seconds.
/// </summary>
public sealed class ThumbnailQueue
{
	private const int WatchedTier      = 0;
	private const int HousekeepingTier = 1;

	// a game whose page keeps yielding nothing gets dropped until restart, so a permanently
	// image-less thread can never turn the sweep into an endless rescrape loop
	private const int MaxFailedAttempts = 3;

	private readonly object _gate = new();
	private readonly PriorityQueue<ulong, (int Tier, long NewestFirst)> _queue = new();

	// best-known priority per game; PriorityQueue cannot update in place, so better enqueues leave
	// stale heap entries behind that TryDequeue skips by priority mismatch
	private readonly Dictionary<ulong, (int Tier, long NewestFirst)> _pending = new();
	private readonly Dictionary<ulong, int>                          _failures = new();

	public void EnqueueWatched(IEnumerable<(ulong GameId, DateTime LastUpdated)> games)
		=> Enqueue(games, WatchedTier);

	public void EnqueueHousekeeping(IEnumerable<(ulong GameId, DateTime LastUpdated)> games)
		=> Enqueue(games, HousekeepingTier);

	public bool TryDequeue(out ulong gameId)
	{
		lock (_gate)
		{
			while (_queue.TryDequeue(out var id, out var priority))
			{
				// superseded by a later, better enqueue (or already handed out) — drop the stale entry
				if (!_pending.TryGetValue(id, out var current) || current != priority) continue;
				_pending.Remove(id);
				gameId = id;
				return true;
			}

			gameId = 0;
			return false;
		}
	}

	public void MarkFailed(ulong gameId)
	{
		lock (_gate)
		{
			_failures[gameId] = _failures.GetValueOrDefault(gameId) + 1;
		}
	}

	internal bool Contains(ulong gameId)
	{
		lock (_gate)
		{
			return _pending.ContainsKey(gameId);
		}
	}

	private void Enqueue(IEnumerable<(ulong GameId, DateTime LastUpdated)> games, int tier)
	{
		lock (_gate)
		{
			foreach (var (gameId, lastUpdated) in games)
			{
				if (_failures.GetValueOrDefault(gameId) >= MaxFailedAttempts) continue;
				var priority = (Tier: tier, NewestFirst: -lastUpdated.Ticks);
				// skip anything that is not an upgrade: never accept a worse tier (a fresher housekeeping
				// sweep must not demote a watched game), and within the same tier only a newer date wins
				if (_pending.TryGetValue(gameId, out var current) &&
				    (priority.Tier > current.Tier || (priority.Tier == current.Tier && priority.NewestFirst >= current.NewestFirst))) continue;
				_pending[gameId] = priority;
				_queue.Enqueue(gameId, priority);
			}
		}
	}
}
