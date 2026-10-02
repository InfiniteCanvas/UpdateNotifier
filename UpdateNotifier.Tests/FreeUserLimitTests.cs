using Microsoft.EntityFrameworkCore;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Tests;

[Collection(DatabaseFixture.CollectionName)]
public sealed class FreeUserLimitTests(DatabaseFixture fixture)
{
	private const ulong GameIdBase = 4_000_000_000;
	private const ulong UserIdBase = 4_100_000_000;

	// per-test gameId stride; must exceed the seeded at-limit range (FREE_USER_LIMIT - 1 games)
	// with room to spare for the unseeded probe game, or tests collide on Games.GameId
	private static readonly ulong GameIdStride = (ulong) Config.FREE_USER_LIMIT * 10;

	private static async Task<(string Hash, ulong AccountId)> SeedUserAtLimitAsync(DatabaseFixture fixture, ulong userId, ulong gameIdBase)
	{
		await using var db = fixture.CreateContext();
		Assert.True(db.AddUser(userId, $"user-{userId}"));

		// The limit check rejects when existing + incoming >= FREE_USER_LIMIT, so a user at the
		// limit has FREE_USER_LIMIT - 1 entries. Watchlist rows need real Games rows (FK).
		// Each test passes its own gameIdBase so seeded games never collide across tests.
		var accountId = await db.Users.Where(u => u.UserId == userId).Select(u => u.AccountId).SingleAsync();
		for (var i = 0; i < Config.FREE_USER_LIMIT - 1; i++)
		{
			var gameId = gameIdBase + (ulong) i;
			db.Games.Add(new Game(gameId, $"Game {i}", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), $"https://f95zone.to/threads/{gameId}"));
			db.Watchlist.Add(new WatchlistEntry { AccountId = accountId, GameId = gameId });
		}

		await db.SaveChangesAsync();

		var hash = await db.Users.Where(u => u.UserId == userId).Select(u => u.Account!.Hash).SingleAsync();
		return (hash, accountId);
	}

	[Fact]
	public async Task UnprivilegedUser_AtLimit_IsRejectedWithoutHttp()
	{
		const ulong userId = UserIdBase + 1;
		const ulong gameIdBase = GameIdBase;
		var (hash, accountId) = await SeedUserAtLimitAsync(fixture, userId, gameIdBase);

		await using var db = fixture.CreateContext();
		// The limit check runs before any URL sanitizing or scraping, so no game needs to exist for this url.
		var (success, response) = await db.TrackGames(hash, [$"https://f95zone.to/threads/limit-hit.{gameIdBase + 500}/"], privileged: false);

		Assert.False(success);
		Assert.Contains("patreon.com/F95UpdateNotifier", response);
		Assert.Equal(Config.FREE_USER_LIMIT - 1, await db.Watchlist.CountAsync(w => w.AccountId == accountId));
	}

	[Fact]
	public async Task PrivilegedUser_AtLimit_BypassesLimit()
	{
		const ulong userId = UserIdBase + 101;
		var gameIdBase = GameIdBase + GameIdStride;
		var newGameId = gameIdBase + GameIdStride / 2;
		var (hash, accountId) = await SeedUserAtLimitAsync(fixture, userId, gameIdBase);
		// Pre-seed the target game so TrackGames never tries to scrape its thread page.
		await using (var seed = fixture.CreateContext())
		{
			seed.Games.Add(new Game(newGameId, "Preseeded", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), $"https://f95zone.to/threads/{newGameId}"));
			await seed.SaveChangesAsync();
		}

		await using var db = fixture.CreateContext();
		var (success, response) = await db.TrackGames(hash, [$"https://f95zone.to/threads/preseeded.{newGameId}/"], privileged: true);

		Assert.True(success);
		Assert.Contains("Games added", response);
		Assert.Equal(Config.FREE_USER_LIMIT, await db.Watchlist.CountAsync(w => w.AccountId == accountId));
	}

	[Fact]
	public async Task TrackGames_UnknownHash_FailsWithoutTouchingTheDatabase()
	{
		const ulong userId = UserIdBase + 201;
		var gameIdBase = GameIdBase + 2 * GameIdStride;
		var hash = await SeedUserAtLimitAsync(fixture, userId, gameIdBase);

		await using var db = fixture.CreateContext();
		var (success, response) = await db.TrackGames("NO-SUCH-HASH", [$"https://f95zone.to/threads/unknown.{gameIdBase + 500}/"], privileged: false);

		Assert.False(success);
		Assert.Equal("User was not found", response);
	}
}
