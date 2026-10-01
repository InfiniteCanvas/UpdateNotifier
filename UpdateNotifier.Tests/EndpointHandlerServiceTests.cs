using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Data.Requests;
using UpdateNotifier.Services;

namespace UpdateNotifier.Tests;

[Collection(DatabaseFixture.CollectionName)]
public sealed class EndpointHandlerServiceTests(DatabaseFixture fixture)
{
	private const ulong GameIdBase = 3_000_000_000;
	private const ulong UserIdBase = 3_100_000_000;

	private static EndpointHandlerService CreateHandler(DataContext db, FakeDmSender sender, FakePrivilegeChecker privileges)
		=> new(db,
		       Microsoft.Extensions.Logging.Abstractions.NullLogger<EndpointHandlerService>.Instance,
		       privileges,
		       sender);

	/// <summary>Creates the account + linked user the way production does, and returns the account's hash and id.</summary>
	private static async Task<(string Hash, ulong AccountId)> SeedUserAsync(DatabaseFixture fixture, ulong userId)
	{
		await using (var db = fixture.CreateContext())
		{
			Assert.True(db.AddUser(userId, $"user-{userId}"));
		}

		await using var verify = fixture.CreateContext();
		var account = await verify.Users.Where(u => u.UserId == userId)
		                          .Select(u => new { u.Account!.Hash, u.AccountId })
		                          .SingleAsync();
		Assert.Matches("^[0-9A-F]{40}$", account.Hash);
		return (account.Hash, account.AccountId);
	}

	private static async Task SeedGameAsync(DatabaseFixture fixture, ulong gameId, string title)
	{
		await using var db = fixture.CreateContext();
		db.Games.Add(new Game(gameId, title, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), $"https://f95zone.to/threads/{gameId}"));
		await db.SaveChangesAsync();
	}

	private static string ThreadUrl(ulong gameId)
		=> $"https://f95zone.to/threads/some-game.{gameId}/";

	[Fact]
	public async Task AddGame_UnknownUserHash_ReturnsNotFound()
	{
		await using var db = fixture.CreateContext();
		var handler = CreateHandler(db, new FakeDmSender(), new FakePrivilegeChecker(false));

		var result = await handler.AddGameAsync(new GameAddRequest { ThreadUrl = ThreadUrl(1), UserHash = "NO-SUCH-HASH" });

		Assert.Equal(404, ((IStatusCodeHttpResult) result).StatusCode);
	}

	[Fact]
	public async Task RemoveGame_UnknownUserHash_ReturnsNotFound()
	{
		await using var db = fixture.CreateContext();
		var handler = CreateHandler(db, new FakeDmSender(), new FakePrivilegeChecker(false));

		var result = await handler.RemoveGameAsync(new GameAddRequest { ThreadUrl = ThreadUrl(1), UserHash = "NO-SUCH-HASH" });

		Assert.Equal(404, ((IStatusCodeHttpResult) result).StatusCode);
	}

	[Fact]
	public async Task AddGame_ExistingGame_AddsWatchlistEntryAndReturnsOk()
	{
		const ulong userId = UserIdBase + 1;
		const ulong gameId = GameIdBase + 1;
		var (hash, accountId) = await SeedUserAsync(fixture, userId);
		await SeedGameAsync(fixture, gameId, "Seeded Game");

		await using var db = fixture.CreateContext();
		var sender = new FakeDmSender();
		var handler = CreateHandler(db, sender, new FakePrivilegeChecker(false));

		var result = await handler.AddGameAsync(new GameAddRequest { ThreadUrl = ThreadUrl(gameId), UserHash = hash });

		Assert.Equal(200, ((IStatusCodeHttpResult) result).StatusCode);
		Assert.Contains("Games added", Assert.IsType<string>(((IValueHttpResult) result).Value));

		await using var verify = fixture.CreateContext();
		Assert.True(await verify.Watchlist.AnyAsync(w => w.AccountId == accountId && w.GameId == gameId));
		Assert.Empty(sender.Sends);
	}

	[Fact]
	public async Task AddGame_WithDiscordNotification_SendsDmToLinkedUser()
	{
		const ulong userId = UserIdBase + 2;
		const ulong gameId = GameIdBase + 2;
		var (hash, _) = await SeedUserAsync(fixture, userId);
		await SeedGameAsync(fixture, gameId, "Seeded Game");

		await using var db = fixture.CreateContext();
		var sender = new FakeDmSender();
		var handler = CreateHandler(db, sender, new FakePrivilegeChecker(false));

		var result = await handler.AddGameAsync(new GameAddRequest { ThreadUrl = ThreadUrl(gameId), UserHash = hash, DiscordNotification = true });

		Assert.Equal(200, ((IStatusCodeHttpResult) result).StatusCode);
		var send = Assert.Single(sender.Sends);
		Assert.Equal(userId, send.UserId);
		Assert.Contains("Games added", send.Message);
	}

	[Fact]
	public async Task RemoveGame_ExistingEntry_ReturnsOkAndRemovesIt()
	{
		const ulong userId = UserIdBase + 3;
		const ulong gameId = GameIdBase + 3;
		var (hash, accountId) = await SeedUserAsync(fixture, userId);
		await SeedGameAsync(fixture, gameId, "Seeded Game");

		await using (var db = fixture.CreateContext())
		{
			db.Watchlist.Add(new WatchlistEntry { AccountId = accountId, GameId = gameId });
			await db.SaveChangesAsync();
		}

		await using var db2 = fixture.CreateContext();
		var handler = CreateHandler(db2, new FakeDmSender(), new FakePrivilegeChecker(false));

		var result = await handler.RemoveGameAsync(new GameAddRequest { ThreadUrl = ThreadUrl(gameId), UserHash = hash });

		Assert.Equal(200, ((IStatusCodeHttpResult) result).StatusCode);

		await using var verify = fixture.CreateContext();
		Assert.False(await verify.Watchlist.AnyAsync(w => w.AccountId == accountId && w.GameId == gameId));
	}

	[Fact]
	public async Task GetWatchedGames_ReturnsWatchedGameIds()
	{
		const ulong userId = UserIdBase + 4;
		const ulong gameA = GameIdBase + 4;
		const ulong gameB = GameIdBase + 5;
		var (hash, accountId) = await SeedUserAsync(fixture, userId);
		await SeedGameAsync(fixture, gameA, "Game A");
		await SeedGameAsync(fixture, gameB, "Game B");

		await using (var db = fixture.CreateContext())
		{
			db.Watchlist.Add(new WatchlistEntry { AccountId = accountId, GameId = gameA });
			db.Watchlist.Add(new WatchlistEntry { AccountId = accountId, GameId = gameB });
			await db.SaveChangesAsync();
		}

		await using var db2 = fixture.CreateContext();
		var handler = CreateHandler(db2, new FakeDmSender(), new FakePrivilegeChecker(false));

		var result = await handler.GetWatchedGamesAsync(hash);

		Assert.Equal(200, ((IStatusCodeHttpResult) result).StatusCode);
		var gameIds = Assert.IsAssignableFrom<IEnumerable<ulong>>(((IValueHttpResult) result).Value).OrderBy(id => id).ToList();
		Assert.Equal([gameA, gameB], gameIds);
	}

	[Fact]
	public async Task GetWatchedGames_UnknownUserHash_ReturnsNotFound()
	{
		await using var db = fixture.CreateContext();
		var handler = CreateHandler(db, new FakeDmSender(), new FakePrivilegeChecker(false));

		var result = await handler.GetWatchedGamesAsync("NO-SUCH-HASH");

		Assert.Equal(404, ((IStatusCodeHttpResult) result).StatusCode);
	}
}
