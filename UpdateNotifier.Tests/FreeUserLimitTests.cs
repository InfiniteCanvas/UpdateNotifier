using Microsoft.EntityFrameworkCore;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Tests;

[Collection(DatabaseFixture.CollectionName)]
public sealed class FreeUserLimitTests(DatabaseFixture fixture)
{
	private const ulong GameIdBase = 4_000_000_000;
	private const ulong UserIdBase = 4_100_000_000;

	private static async Task SeedUserAtLimitAsync(DatabaseFixture fixture, ulong userId, ulong gameIdBase)
	{
		await using var db = fixture.CreateContext();
		db.Users.Add(new User(userId));
		// The limit check rejects when existing + incoming >= FREE_USER_LIMIT, so a user at the
		// limit has FREE_USER_LIMIT - 1 entries. Watchlist rows need real Games rows (FK).
		// Each test passes its own gameIdBase so seeded games never collide across tests.
		for (var i = 0; i < Config.FREE_USER_LIMIT - 1; i++)
		{
			var gameId = gameIdBase + (ulong) i;
			db.Games.Add(new Game(gameId, $"Game {i}", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), $"https://f95zone.to/threads/{gameId}"));
			db.Watchlist.Add(new WatchlistEntry { UserId = userId, GameId = gameId });
		}

		await db.SaveChangesAsync();
	}

	[Fact]
	public async Task UnprivilegedUser_AtLimit_IsRejectedWithoutHttp()
	{
		const ulong userId = UserIdBase + 1;
		const ulong gameIdBase = GameIdBase;
		await SeedUserAtLimitAsync(fixture, userId, gameIdBase);

		await using var db = fixture.CreateContext();
		// The limit check runs before any URL sanitizing or scraping, so no game needs to exist for this url.
		var (success, response) = await db.AddGames(userId, privileged: false, [$"https://f95zone.to/threads/limit-hit.{gameIdBase + 500}/"]);

		Assert.False(success);
		Assert.Contains("patreon.com/F95UpdateNotifier", response);
		Assert.Equal(Config.FREE_USER_LIMIT - 1, await db.Watchlist.CountAsync(w => w.UserId == userId));
	}

	[Fact]
	public async Task PrivilegedUser_AtLimit_BypassesLimit()
	{
		const ulong userId = UserIdBase + 101;
		const ulong gameIdBase = GameIdBase + 10_000;
		const ulong newGameId = gameIdBase + 500;
		await SeedUserAtLimitAsync(fixture, userId, gameIdBase);
		// Pre-seed the target game so AddGames never tries to scrape its thread page.
		await using (var seed = fixture.CreateContext())
		{
			seed.Games.Add(new Game(newGameId, "Preseeded", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), $"https://f95zone.to/threads/{newGameId}"));
			await seed.SaveChangesAsync();
		}

		await using var db = fixture.CreateContext();
		var (success, response) = await db.AddGames(userId, privileged: true, [$"https://f95zone.to/threads/preseeded.{newGameId}/"]);

		Assert.True(success);
		Assert.Contains("Games added", response);
		Assert.Equal(Config.FREE_USER_LIMIT, await db.Watchlist.CountAsync(w => w.UserId == userId));
	}
}
