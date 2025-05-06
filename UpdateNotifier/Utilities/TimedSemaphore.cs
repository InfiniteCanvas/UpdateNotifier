namespace UpdateNotifier.Utilities;

/// <summary>
///     It's a SemaphoreSlim, but it releases automatically after <paramref name="releaseTime" /> after calling WaitAsync.
/// </summary>
/// <param name="initialCount">Initial count, as with SemaphoreSlim</param>
/// <param name="maximumCount">Maximum count, as with SemaphoreSlim</param>
/// <param name="releaseTime">Time it takes for a handle to release</param>
public class TimedSemaphore(int initialCount, int maximumCount, TimeSpan releaseTime) : IDisposable
{
	private readonly SemaphoreSlim _semaphore = new(initialCount, maximumCount);

	public void Dispose() => _semaphore.Dispose();

	public async Task WaitAsync(CancellationToken ct)
	{
		await _semaphore.WaitAsync(ct);
		_ = Task.Delay(releaseTime, ct).ContinueWith(_ => _semaphore.Release(), TaskScheduler.Current);
	}
}