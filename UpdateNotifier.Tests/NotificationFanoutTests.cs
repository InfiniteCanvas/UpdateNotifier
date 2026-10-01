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
	private const ulong AccountIdBase = 2_200_000_000;

	/// <summary>
	///     Seeds a watched game plus an account watching it; a null userId seeds a web-only
	///     account (no linked Discord identity). Returns the account id.
	/// </summary>
	private static async Task<ulong> SeedWatcherAsync(DatabaseFixture fixture, ulong gameId, ulong? userId)
	{
		await using var db = fixture.CreateContext();
		var account = new Account
		{
			// stable, unique, 40-char uppercase hex - the same shape AddUser generates
			Hash = ((userId ?? AccountIdBase) + gameId).ToString("X40"),
			CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
		};
		db.Accounts.Add(account);
		await db.SaveChangesAsync(); // real key first, so the dependents can reference it directly

		if (userId != null)
			db.Users.Add(new User(userId.Value) { AccountId = account.AccountId, DiscordUsername = $"user-{userId}" });
		db.Watchlist.Add(new WatchlistEntry { AccountId = account.AccountId, GameId = gameId });
		await db.SaveChangesAsync();
		return account.AccountId;
	}

	private static async Task SeedGameAsync(DatabaseFixture fixture, ulong gameId, DateTime lastUpdated)
	{
		await using var db = fixture.CreateContext();
		db.Games.Add(new Game(gameId, "Title", lastUpdated, $"https://f95zone.to/threads/{gameId}"));
		await db.SaveChangesAsync();
	}

	[Fact]
	public async Task UpdatedGame_NotifiesEveryLinkedWatcherWithGameUrl()
	{
		const ulong gameId = GameIdBase + 1;
		const ulong userA = UserIdBase + 1;
		const ulong userB = UserIdBase + 2;
		var oldDate = new DateTime(2025, 2, 2, 0, 0, 0, DateTimeKind.Utc);
		var newDate = oldDate.AddDays(2);
		var url = $"https://f95zone.to/threads/{gameId}";

		await SeedGameAsync(fixture, gameId, oldDate);
		await SeedWatcherAsync(fixture, gameId, userA);
		await SeedWatcherAsync(fixture, gameId, userB);

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

	[Fact]
	public async Task UpdatedGame_WebOnlyAccountIsSkipped()
	{
		const ulong gameId = GameIdBase + 11;
		const ulong linkedUser = UserIdBase + 11;
		var oldDate = new DateTime(2025, 2, 2, 0, 0, 0, DateTimeKind.Utc);
		var newDate = oldDate.AddDays(2);

		await SeedGameAsync(fixture, gameId, oldDate);
		await SeedWatcherAsync(fixture, gameId, linkedUser);
		var webOnlyAccountId = await SeedWatcherAsync(fixture, gameId, userId: null);

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

			await WaitForSendsAsync(sender, expected: 1);
			await notifications.StopAsync(CancellationToken.None);
			notifications.Dispose();
		}

		// exactly one DM, addressed to the linked snowflake - the web-only account produced no send
		var send = Assert.Single(sender.Sends);
		Assert.Equal(linkedUser, send.UserId);
		Assert.NotEqual(0UL, webOnlyAccountId);
	}

	[Fact]
	public async Task FailingDm_DoesNotKillTheNotificationLoop()
	{
		const ulong gameId = GameIdBase + 21;
		const ulong userA = UserIdBase + 21;
		const ulong userB = UserIdBase + 22;
		var oldDate = new DateTime(2025, 2, 2, 0, 0, 0, DateTimeKind.Utc);
		var newDate = oldDate.AddDays(2);

		await SeedGameAsync(fixture, gameId, oldDate);
		await SeedWatcherAsync(fixture, gameId, userA);
		await SeedWatcherAsync(fixture, gameId, userB);

		var sender = new FlakyDmSender();

		await using (var db = fixture.CreateContext())
		{
			var monitor = new RssMonitorService(NullLogger<RssMonitorService>.Instance,
			                                    fixture.Services.GetRequiredService<Config>(),
			                                    db,
			                                    ThrowingHttpClientFactory.Instance);
			var notifications = new NotificationService(NullLogger<NotificationService>.Instance, monitor, sender);
			await notifications.StartAsync(CancellationToken.None);

			await monitor.CheckFeed(RssFeed.MakeFeed((gameId, "Title", newDate)), CancellationToken.None);

			// the throwing send happens first; the loop must survive it and deliver the second one
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			while (sender.Calls < 2)
				await Task.Delay(25, cts.Token);

			await notifications.StopAsync(CancellationToken.None);
			notifications.Dispose();
		}

		Assert.Equal(2, sender.Calls);
		var delivered = Assert.Single(sender.Sends);
		Assert.Contains($"https://f95zone.to/threads/{gameId}", delivered.Message);
	}

	private static async Task WaitForSendsAsync(FakeDmSender sender, int expected)
	{
		// The notification channel is drained asynchronously; poll with a deadline instead of sleeping.
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		while (sender.Sends.Count < expected)
			await Task.Delay(25, cts.Token);
	}
}
