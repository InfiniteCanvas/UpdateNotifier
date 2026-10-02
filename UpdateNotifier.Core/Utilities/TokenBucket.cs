namespace UpdateNotifier.Utilities;

/// <summary>
///     Classic token-bucket rate limiter: holds at most <paramref name="capacity" /> tokens, refilled
///     continuously at <paramref name="refillPerSecond" />. Allows a controlled burst up to capacity
///     while capping the sustained rate — unlike TimedSemaphore, which only gates concurrency.
/// </summary>
public sealed class TokenBucket(int capacity, double refillPerSecond, TimeProvider? timeProvider = null)
{
	private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
	private readonly object       _gate         = new();
	private          double       _tokens       = capacity;
	private          DateTimeOffset _lastRefill;

	public async Task WaitForTokenAsync(CancellationToken ct)
	{
		while (true)
		{
			TimeSpan delay;
			lock (_gate)
			{
				Refill(_timeProvider.GetUtcNow());
				if (_tokens >= 1)
				{
					_tokens -= 1;
					return;
				}

				// tokens are short by (1 - _tokens); that many seconds of refill stand between us and the next token
				delay = TimeSpan.FromSeconds((1 - _tokens) / refillPerSecond);
			}

			await Task.Delay(delay, _timeProvider, ct);
		}
	}

	public bool TryAcquire()
	{
		lock (_gate)
		{
			Refill(_timeProvider.GetUtcNow());
			if (_tokens < 1) return false;
			_tokens -= 1;
			return true;
		}
	}

	private void Refill(DateTimeOffset now)
	{
		if (now <= _lastRefill) return;
		_tokens = Math.Min(capacity, _tokens + (now - _lastRefill).TotalSeconds * refillPerSecond);
		_lastRefill = now;
	}
}
