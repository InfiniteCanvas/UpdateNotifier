using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Tests;

[Collection(DatabaseFixture.CollectionName)]
public sealed class UpdateDetectionTests(DatabaseFixture fixture)
{
	// Each test class uses its own disjoint range of game ids so tests can share one database.
	private const ulong IdBase = 1_000_000_000;

	private RssMonitorService CreateMonitor(DataContext db)
		=> new(NullLogger<RssMonitorService>.Instance,
		       fixture.Services.GetRequiredService<Config>(),
		       db,
		       ThrowingHttpClientFactory.Instance);

	[Fact]
	public async Task NewerFeedItem_UpdatesRowAndRaisesEvent()
	{
		const ulong gameId = IdBase + 1;
		var oldDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var newDate = oldDate.AddDays(1);

		await using (var db = fixture.CreateContext())
		{
			db.Games.Add(new Game(gameId, "Old Title", oldDate, $"https://f95zone.to/threads/{gameId}"));
			await db.SaveChangesAsync();
		}

		List<Game>? raised = null;
		await using (var db = fixture.CreateContext())
		{
			var monitor = CreateMonitor(db);
			monitor.GamesUpdatedEvent += games => raised = games;
			await monitor.CheckFeed(RssFeed.MakeFeed((gameId, "New Title", newDate, null)), CancellationToken.None);
		}

		Assert.NotNull(raised);
		var updated = Assert.Single(raised);
		Assert.Equal(gameId, updated.GameId);

		await using (var db = fixture.CreateContext())
		{
			var game = await db.Games.SingleAsync(g => g.GameId == gameId);
			Assert.Equal(newDate, game.LastUpdated);
			Assert.Equal("New Title", game.Title);
		}
	}

	[Fact]
	public async Task UnknownFeedItem_IsAddedWithoutEvent()
	{
		const ulong gameId = IdBase + 11;
		var date = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

		List<Game>? raised = null;
		await using (var db = fixture.CreateContext())
		{
			var monitor = CreateMonitor(db);
			monitor.GamesUpdatedEvent += games => raised = games;
			await monitor.CheckFeed(RssFeed.MakeFeed((gameId, "Brand New", date, null)), CancellationToken.None);
		}

		Assert.Null(raised);

		await using (var db = fixture.CreateContext())
		{
			var game = await db.Games.SingleAsync(g => g.GameId == gameId);
			Assert.Equal("Brand New", game.Title);
			Assert.Equal(date, game.LastUpdated);
			Assert.Equal($"https://f95zone.to/threads/{gameId}", game.Url);
		}
	}

	[Fact]
	public async Task UnchangedFeedItem_DoesNotRaiseEvent()
	{
		const ulong gameId = IdBase + 21;
		var date = new DateTime(2025, 3, 3, 8, 30, 0, DateTimeKind.Utc);

		await using (var db = fixture.CreateContext())
		{
			db.Games.Add(new Game(gameId, "Same Title", date, $"https://f95zone.to/threads/{gameId}"));
			await db.SaveChangesAsync();
		}

		List<Game>? raised = null;
		await using (var db = fixture.CreateContext())
		{
			var monitor = CreateMonitor(db);
			monitor.GamesUpdatedEvent += games => raised = games;
			await monitor.CheckFeed(RssFeed.MakeFeed((gameId, "Same Title", date, null)), CancellationToken.None);
		}

		Assert.Null(raised);

		await using (var db = fixture.CreateContext())
		{
			var game = await db.Games.SingleAsync(g => g.GameId == gameId);
			Assert.Equal(date, game.LastUpdated);
		}
	}

	[Fact]
	public async Task NewGameWithThumbnail_StoresThumbnailUrl()
	{
		const ulong gameId = IdBase + 31;
		var date = new DateTime(2025, 6, 15, 9, 0, 0, DateTimeKind.Utc);
		const string thumbnail = "https://example.com/banners/31.png";

		await using (var db = fixture.CreateContext())
		{
			var monitor = CreateMonitor(db);
			await monitor.CheckFeed(RssFeed.MakeFeed((gameId, "Fresh Game", date, thumbnail)), CancellationToken.None);
		}

		await using (var db = fixture.CreateContext())
		{
			var game = await db.Games.SingleAsync(g => g.GameId == gameId);
			Assert.Equal(thumbnail, game.ThumbnailUrl);
		}
	}

	[Fact]
	public async Task NewerFeedItemWithThumbnail_UpdatesThumbnailUrl()
	{
		const ulong gameId = IdBase + 41;
		var oldDate = new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc);
		var newDate = oldDate.AddDays(3);
		const string oldThumbnail = "https://example.com/banners/41-old.png";
		const string newThumbnail = "https://example.com/banners/41-new.png";

		await using (var db = fixture.CreateContext())
		{
			db.Games.Add(new Game(gameId, "Old Title", oldDate, $"https://f95zone.to/threads/{gameId}", oldThumbnail));
			await db.SaveChangesAsync();
		}

		await using (var db = fixture.CreateContext())
		{
			var monitor = CreateMonitor(db);
			await monitor.CheckFeed(RssFeed.MakeFeed((gameId, "New Title", newDate, newThumbnail)), CancellationToken.None);
		}

		await using (var db = fixture.CreateContext())
		{
			var game = await db.Games.SingleAsync(g => g.GameId == gameId);
			Assert.Equal(newDate, game.LastUpdated);
			Assert.Equal("New Title", game.Title);
			Assert.Equal(newThumbnail, game.ThumbnailUrl);
		}
	}

	[Fact]
	public async Task UnchangedFeedItemWithThumbnail_BackfillsThumbnailWithoutEvent()
	{
		const ulong gameId = IdBase + 51;
		var date = new DateTime(2025, 5, 5, 16, 45, 0, DateTimeKind.Utc);
		const string thumbnail = "https://example.com/banners/51.png";

		await using (var db = fixture.CreateContext())
		{
			db.Games.Add(new Game(gameId, "Same Title", date, $"https://f95zone.to/threads/{gameId}"));
			await db.SaveChangesAsync();
		}

		List<Game>? raised = null;
		await using (var db = fixture.CreateContext())
		{
			var monitor = CreateMonitor(db);
			monitor.GamesUpdatedEvent += games => raised = games;
			await monitor.CheckFeed(RssFeed.MakeFeed((gameId, "Same Title", date, thumbnail)), CancellationToken.None);
		}

		Assert.Null(raised);

		await using (var db = fixture.CreateContext())
		{
			var game = await db.Games.SingleAsync(g => g.GameId == gameId);
			Assert.Equal(thumbnail, game.ThumbnailUrl);
			Assert.Equal(date, game.LastUpdated);
			Assert.Equal("Same Title", game.Title);
		}
	}

	[Fact]
	public async Task NewerFeedItemWithoutThumbnail_PreservesThumbnailUrl()
	{
		const ulong gameId = IdBase + 61;
		var oldDate = new DateTime(2025, 7, 7, 0, 0, 0, DateTimeKind.Utc);
		var newDate = oldDate.AddDays(1);
		const string thumbnail = "https://example.com/banners/61.png";

		await using (var db = fixture.CreateContext())
		{
			db.Games.Add(new Game(gameId, "Old Title", oldDate, $"https://f95zone.to/threads/{gameId}", thumbnail));
			await db.SaveChangesAsync();
		}

		await using (var db = fixture.CreateContext())
		{
			var monitor = CreateMonitor(db);
			await monitor.CheckFeed(RssFeed.MakeFeed((gameId, "New Title", newDate, null)), CancellationToken.None);
		}

		await using (var db = fixture.CreateContext())
		{
			var game = await db.Games.SingleAsync(g => g.GameId == gameId);
			Assert.Equal(newDate, game.LastUpdated);
			Assert.Equal("New Title", game.Title);
			Assert.Equal(thumbnail, game.ThumbnailUrl);
		}
	}

	[Fact]
	public async Task NewGameWithoutThumbnail_LeavesThumbnailUrlNull()
	{
		const ulong gameId = IdBase + 71;
		var date = new DateTime(2025, 8, 8, 10, 15, 0, DateTimeKind.Utc);

		await using (var db = fixture.CreateContext())
		{
			var monitor = CreateMonitor(db);
			await monitor.CheckFeed(RssFeed.MakeFeed((gameId, "No Banner", date, null)), CancellationToken.None);
		}

		await using (var db = fixture.CreateContext())
		{
			var game = await db.Games.SingleAsync(g => g.GameId == gameId);
			Assert.Null(game.ThumbnailUrl);
		}
	}
}
