using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Tests;

[Collection(DatabaseFixture.CollectionName)]
public sealed class NotificationFanoutTests(DatabaseFixture fixture)
{
	private const ulong GameIdBase  = 2_000_000_000;
	private const ulong UserIdBase  = 2_100_000_000;

	[Fact]
	public async Task UpdatedGame_NotifiesEveryWatcherWithGameUrl()
	{
		const ulong gameId = GameIdBase + 1;
		const ulong userA = UserIdBase + 1;
		const ulong userB = UserIdBase + 2;
		var oldDate = new DateTime(2025, 2, 2, 0, 0, 0, DateTimeKind.Utc);
		var newDate = oldDate.AddDays(2);
		var url = $"https://f95zone.to/threads/{gameId}";

		await using (var db = fixture.CreateContext())
		{
			db.Games.Add(new Game(gameId, "Title", oldDate, url));
			db.Users.Add(new User(userA));
			db.Users.Add(new User(userB));
			db.Watchlist.Add(new WatchlistEntry { UserId = userA, GameId = gameId });
			db.Watchlist.Add(new WatchlistEntry { UserId = userB, GameId = gameId });
			await db.SaveChangesAsync();
		}

		var sender = new FakeDmSender();

		await using (var db = fixture.CreateContext())
		{
			var monitor = new RssMonitorService(NullLogger<RssMonitorService>.Instance,
			                                    fixture.Services.GetRequiredService<Config>(),
			                                    db,
			                                    ThrowingHttpClientFactory.Instance);
			var notifications = new NotificationService(NullLogger<NotificationService>.Instance, monitor, sender);
			await notifications.StartAsync(CancellationToken.None);

			await monitor.CheckFeed(RssFeed.MakeFeed((gameId, "Title", newDate)), CancellationToken.None);

			await WaitForSendsAsync(sender, expected: 2);
			await notifications.StopAsync(CancellationToken.None);
			notifications.Dispose();
		}

		Assert.Equal(2, sender.Sends.Count);
		Assert.Equal([userA, userB], sender.Sends.Select(send => send.UserId).OrderBy(id => id));
		Assert.All(sender.Sends, send => Assert.Contains(url, send.Message));
	}

	private static async Task WaitForSendsAsync(FakeDmSender sender, int expected)
	{
		// The notification channel is drained asynchronously; poll with a deadline instead of sleeping.
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		while (sender.Sends.Count < expected)
			await Task.Delay(25, cts.Token);
	}
}
