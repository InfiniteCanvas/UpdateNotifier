using UpdateNotifier.Utilities;

namespace UpdateNotifier.Tests;

public sealed class TokenBucketTests
{
	[Fact]
	public void TryAcquire_WithinCapacity_BurstsUpToCapacity()
	{
		var bucket = new TokenBucket(5, 1);

		for (var i = 0; i < 5; i++)
			Assert.True(bucket.TryAcquire());

		Assert.False(bucket.TryAcquire());
	}

	[Fact]
	public void TryAcquire_AfterClockAdvances_RefillsToken()
	{
		var time = new MutableTimeProvider();
		var bucket = new TokenBucket(5, 10, time); // 10 tokens per second

		for (var i = 0; i < 5; i++)
			Assert.True(bucket.TryAcquire());
		Assert.False(bucket.TryAcquire());

		time.NowValue += TimeSpan.FromMilliseconds(100); // exactly one token's worth of refill

		Assert.True(bucket.TryAcquire());
	}

	[Fact]
	public void TryAcquire_WithoutClockAdvance_StaysExhausted()
	{
		var time = new MutableTimeProvider();
		var bucket = new TokenBucket(5, 10, time);

		for (var i = 0; i < 5; i++)
			Assert.True(bucket.TryAcquire());

		// no elapsed time means no refill credit, however fast the rate
		Assert.False(bucket.TryAcquire());
	}
}
