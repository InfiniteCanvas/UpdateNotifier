using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Extensions;
using UpdateNotifier.Services;

namespace UpdateNotifier.Tests;

[Collection(DatabaseFixture.CollectionName)]
public sealed class WebAuthTests(DatabaseFixture fixture)
{
	private const ulong GameIdBase = 3_200_000_000;
	private const ulong UserIdBase = 3_300_000_000;

	private const string Password = "password123";

	private WebAuthService CreateService(DataContext db)
		=> new(db, NullLogger<WebAuthService>.Instance);

	private async Task<AuthResult> RegisterAsync(string username, string password = Password)
	{
		await using var db = fixture.CreateContext();
		return await CreateService(db).RegisterAsync(username, password);
	}

	private async Task<AuthResult> LoginAsync(string username, string password)
	{
		await using var db = fixture.CreateContext();
		return await CreateService(db).LoginAsync(username, password);
	}

	private static async Task SeedGameAsync(DatabaseFixture fixture, ulong gameId, DateTime? lastUpdated = null)
	{
		await using var db = fixture.CreateContext();
		db.Games.Add(new Game(gameId, $"Game {gameId}",
		                       lastUpdated ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
		                       $"https://f95zone.to/threads/{gameId}"));
		await db.SaveChangesAsync();
	}

	private static async Task SeedWatchlistAsync(DatabaseFixture fixture, ulong accountId, params ulong[] gameIds)
	{
		await using var db = fixture.CreateContext();
		foreach (var gameId in gameIds)
			db.Watchlist.Add(new WatchlistEntry { AccountId = accountId, GameId = gameId });
		await db.SaveChangesAsync();
	}

	private static async Task SeedDiscordUserAsync(DatabaseFixture fixture, ulong accountId, ulong userId, string username = "discord-user")
	{
		await using var db = fixture.CreateContext();
		db.Users.Add(new User(userId) { AccountId = accountId, DiscordUsername = username });
		await db.SaveChangesAsync();
	}

	private static async Task<ulong> EnableDiscordUserAsync(DatabaseFixture fixture, ulong userId, string username)
	{
		await using var db = fixture.CreateContext();
		Assert.True(db.AddUser(userId, username));
		return await db.Users.Where(u => u.UserId == userId).Select(u => u.AccountId).SingleAsync();
	}

	private static string HashToken(string token)
		=> Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

	[Fact]
	public async Task Register_CreatesValidatingSession_AndMeShape()
	{
		var result = await RegisterAsync("shape-tester");

		Assert.True(result.Success);
		Assert.Equal(AuthError.None, result.Error);
		Assert.Equal("shape-tester", result.Username);
		Assert.NotEqual(0ul, result.AccountId);
		Assert.Matches("^[0-9A-F]{64}$", result.Token); // 32 random bytes, hex encoded
		Assert.True(result.ExpiresAt > DateTime.UtcNow.AddDays(29));

		await using var db = fixture.CreateContext();
		var service = CreateService(db);

		var session = await service.ValidateSessionAsync(result.Token);
		Assert.NotNull(session);
		Assert.Equal(result.AccountId, session!.AccountId);

		// the "me" shape: web row with username, account with hash, no Discord link
		var web = await service.GetAccountAsync(result.AccountId);
		Assert.NotNull(web);
		Assert.Equal("shape-tester", web!.Username);
		Assert.NotNull(web.Account);
		Assert.Matches("^[0-9A-F]{40}$", web.Account!.Hash);
		Assert.Null(web.Account.User);
	}

	[Fact]
	public async Task Register_DuplicateUsername_IsRejectedCaseInsensitively()
	{
		Assert.True((await RegisterAsync("Alice")).Success);

		var duplicate = await RegisterAsync("alice"); // NOCASE-unique index must catch this

		Assert.False(duplicate.Success);
		Assert.Equal(AuthError.UsernameTaken, duplicate.Error);
	}

	[Fact]
	public async Task Register_InvalidInput_ReturnsUserSafeError()
	{
		var shortName = await RegisterAsync("ab");
		var badChars = await RegisterAsync("has spaces!");
		var shortPassword = await RegisterAsync("valid-name", "short");

		foreach (var result in new[] { shortName, badChars, shortPassword })
		{
			Assert.False(result.Success);
			Assert.Equal(AuthError.InvalidInput, result.Error);
			Assert.False(string.IsNullOrEmpty(result.Message));
		}
	}

	[Fact]
	public async Task Login_UnknownUser_ReturnsGenericFailure()
	{
		var result = await LoginAsync("no-such-user", "whatever123");

		Assert.False(result.Success);
		Assert.Equal(AuthError.InvalidCredentials, result.Error);
		Assert.Equal("Invalid username or password.", result.Message);
	}

	[Fact]
	public async Task Login_WrongPassword_ReturnsSameMessageAsUnknownUser()
	{
		await RegisterAsync("login-tester", Password);

		var wrongPassword = await LoginAsync("login-tester", "wrong-password");
		var unknown = await LoginAsync("no-such-user-login", "wrong-password");

		Assert.False(wrongPassword.Success);
		Assert.Equal(AuthError.InvalidCredentials, wrongPassword.Error);
		Assert.Equal(unknown.Message, wrongPassword.Message); // must not leak which part failed
	}

	[Fact]
	public async Task Login_CorrectPassword_ReturnsWorkingToken()
	{
		var register = await RegisterAsync("login-success", Password);

		var login = await LoginAsync("login-success", Password);

		Assert.True(login.Success);
		Assert.Equal(register.AccountId, login.AccountId);
		Assert.Matches("^[0-9A-F]{64}$", login.Token);

		await using var db = fixture.CreateContext();
		var session = await CreateService(db).ValidateSessionAsync(login.Token);
		Assert.NotNull(session);
		Assert.Equal(register.AccountId, session!.AccountId);
	}

	[Fact]
	public async Task Logout_DeletesTheSession()
	{
		var register = await RegisterAsync("logout-tester");

		await using var db = fixture.CreateContext();
		var service = CreateService(db);
		await service.LogoutAsync(register.Token);

		Assert.Null(await service.ValidateSessionAsync(register.Token));
	}

	[Fact]
	public async Task DeleteAccount_WrongPassword_ReturnsFalse()
	{
		var register = await RegisterAsync("deleteme-wrongpw");

		await using var db = fixture.CreateContext();
		Assert.False(await CreateService(db).DeleteAccountAsync(register.AccountId, "wrong-password"));
	}

	[Fact]
	public async Task DeleteAccount_CorrectPassword_RemovesEverythingButGames()
	{
		const ulong userId = UserIdBase + 1;
		const ulong gameId = GameIdBase + 1;
		var register = await RegisterAsync("deleteme-rightpw");
		await SeedGameAsync(fixture, gameId);
		await SeedWatchlistAsync(fixture, register.AccountId, gameId);
		await SeedDiscordUserAsync(fixture, register.AccountId, userId);

		await using var db = fixture.CreateContext();
		Assert.True(await CreateService(db).DeleteAccountAsync(register.AccountId, Password));

		await using var verify = fixture.CreateContext();
		Assert.False(await verify.Accounts.AnyAsync(a => a.AccountId == register.AccountId));
		Assert.False(await verify.WebAccounts.AnyAsync(w => w.AccountId == register.AccountId));
		Assert.False(await verify.Watchlist.AnyAsync(w => w.AccountId == register.AccountId));
		Assert.False(await verify.Users.AnyAsync(u => u.UserId == userId));
		Assert.True(await verify.Games.AnyAsync(g => g.GameId == gameId)); // games are shared, they survive
	}

	[Fact]
	public async Task RegenerateHash_ReplacesOldHash_AndKeepsWatchlist()
	{
		const ulong gameId = GameIdBase + 2;
		var register = await RegisterAsync("hash-rotate");
		await SeedGameAsync(fixture, gameId);
		await SeedWatchlistAsync(fixture, register.AccountId, gameId);

		string oldHash;
		await using (var db = fixture.CreateContext())
		{
			oldHash = await db.Accounts.Where(a => a.AccountId == register.AccountId).Select(a => a.Hash).SingleAsync();
		}

		string newHash;
		await using (var db = fixture.CreateContext())
		{
			newHash = await CreateService(db).RegenerateHashAsync(register.AccountId);
		}

		Assert.Matches("^[0-9A-F]{40}$", newHash);
		Assert.NotEqual(oldHash, newHash);

		await using var verify = fixture.CreateContext();
		Assert.Null(await verify.GetAccountByHashAsync(oldHash)); // old hash is dead
		var account = await verify.GetAccountByHashAsync(newHash);
		Assert.NotNull(account);
		Assert.Equal(register.AccountId, account!.AccountId);
		Assert.True(await verify.Watchlist.AnyAsync(w => w.AccountId == register.AccountId && w.GameId == gameId));
	}

	[Fact]
	public async Task ValidateSession_ExpiredSession_ReturnsNullAndPurges()
	{
		var register = await RegisterAsync("expired-session");

		var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
		var tokenHash = HashToken(token);
		await using (var db = fixture.CreateContext())
		{
			db.WebSessions.Add(new WebSession
			{
				TokenHash = tokenHash,
				AccountId = register.AccountId,
				CreatedAt = DateTime.UtcNow.AddDays(-2),
				ExpiresAt = DateTime.UtcNow.AddDays(-1)
			});
			db.LinkCodes.Add(new LinkCode
			{
				Code = "EXPIREDCODE0001",
				AccountId = register.AccountId,
				CreatedAt = DateTime.UtcNow.AddDays(-2),
				ExpiresAt = DateTime.UtcNow.AddDays(-1)
			});
			await db.SaveChangesAsync();
		}

		await using var db2 = fixture.CreateContext();
		Assert.Null(await CreateService(db2).ValidateSessionAsync(token));

		await using var verify = fixture.CreateContext();
		Assert.False(await verify.WebSessions.AnyAsync(s => s.TokenHash == tokenHash)); // purged
		Assert.False(await verify.LinkCodes.AnyAsync(l => l.Code == "EXPIREDCODE0001")); // purged too
	}

	[Fact]
	public async Task ConsumeLinkCode_MergesDiscordAccountIntoWebAccount()
	{
		const ulong userId = UserIdBase + 2;
		const ulong gameAOnly = GameIdBase + 3;
		const ulong gameBOnly = GameIdBase + 4;
		const ulong gameBoth = GameIdBase + 5;

		var web = await RegisterAsync("merger");
		await SeedGameAsync(fixture, gameAOnly);
		await SeedGameAsync(fixture, gameBOnly);
		await SeedGameAsync(fixture, gameBoth);
		var discordAccountId = await EnableDiscordUserAsync(fixture, userId, "discord-old-name");
		await SeedWatchlistAsync(fixture, discordAccountId, gameAOnly, gameBoth);
		await SeedWatchlistAsync(fixture, web.AccountId, gameBOnly, gameBoth);

		string code;
		await using (var db = fixture.CreateContext())
		{
			var link = await CreateService(db).CreateLinkCodeAsync(web.AccountId);
			Assert.NotNull(link);
			Assert.Matches("^[0-9A-F]{40}$", link!.Value.Code);
			Assert.True(link.Value.ExpiresAt > DateTime.UtcNow.AddMinutes(14));
			code = link.Value.Code;
		}

		LinkOutcome outcome;
		await using (var db = fixture.CreateContext())
		{
			outcome = await CreateService(db).ConsumeLinkCodeAsync(code, userId, "discord-new-name");
		}

		Assert.True(outcome.IsSuccess);
		Assert.Equal(LinkOutcomeKind.Merged, outcome.Kind);
		Assert.Equal(1, outcome.GamesMerged); // only gameAOnly was carried over; gameBoth was already tracked

		await using var verify = fixture.CreateContext();
		// sort client-side: the SQLite provider cannot ORDER BY ulong-typed columns
		var watchlist = (await verify.Watchlist.Where(w => w.AccountId == web.AccountId)
		                              .Select(w => w.GameId).ToListAsync())
			.OrderBy(id => id).ToList();
		Assert.Equal([gameAOnly, gameBOnly, gameBoth], watchlist); // the union
		Assert.False(await verify.Accounts.AnyAsync(a => a.AccountId == discordAccountId)); // source account deleted
		var user = await verify.Users.SingleAsync(u => u.UserId == userId);
		Assert.Equal(web.AccountId, user.AccountId);
		Assert.Equal("discord-new-name", user.DiscordUsername);
		Assert.True(await verify.WebAccounts.AnyAsync(w => w.AccountId == web.AccountId)); // web login intact
		Assert.False(await verify.LinkCodes.AnyAsync(l => l.Code == code)); // consumed
	}

	[Fact]
	public async Task ConsumeLinkCode_AlreadyLinkedToSameAccount_IsIdempotent()
	{
		const ulong userId = UserIdBase + 3;
		var web = await RegisterAsync("already-linked");
		await EnableDiscordUserAsync(fixture, userId, "discord-user");

		await using (var db = fixture.CreateContext())
		{
			var service = CreateService(db);
			var first = await service.CreateLinkCodeAsync(web.AccountId);
			Assert.NotNull(first);
			Assert.Equal(LinkOutcomeKind.Merged,
			             (await service.ConsumeLinkCodeAsync(first!.Value.Code, userId, "discord-user")).Kind);

			// linked now: no further codes can be issued (the endpoint maps this to 409)
			Assert.Null(await service.CreateLinkCodeAsync(web.AccountId));
		}

		await using (var db = fixture.CreateContext())
		{
			// forge a second outstanding code to reach the idempotent branch
			db.LinkCodes.Add(new LinkCode
			{
				Code = "FORGED2NDCODE00",
				AccountId = web.AccountId,
				CreatedAt = DateTime.UtcNow,
				ExpiresAt = DateTime.UtcNow.AddMinutes(15)
			});
			await db.SaveChangesAsync();
		}

		await using (var db = fixture.CreateContext())
		{
			var outcome = await CreateService(db).ConsumeLinkCodeAsync("FORGED2NDCODE00", userId, "discord-user");
			Assert.True(outcome.IsSuccess);
			Assert.Equal(LinkOutcomeKind.AlreadyLinked, outcome.Kind);
		}
	}

	[Fact]
	public async Task ConsumeLinkCode_ExpiredCode_IsInvalid()
	{
		const ulong userId = UserIdBase + 4;
		var web = await RegisterAsync("expired-code");
		await EnableDiscordUserAsync(fixture, userId, "discord-user");

		string code;
		await using (var db = fixture.CreateContext())
		{
			var link = await CreateService(db).CreateLinkCodeAsync(web.AccountId);
			Assert.NotNull(link);
			code = link!.Value.Code;
		}

		await using (var db = fixture.CreateContext())
		{
			var linkCode = await db.LinkCodes.SingleAsync(l => l.Code == code);
			linkCode.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
			await db.SaveChangesAsync();
		}

		await using (var db = fixture.CreateContext())
		{
			var outcome = await CreateService(db).ConsumeLinkCodeAsync(code, userId, "discord-user");
			Assert.False(outcome.IsSuccess);
			Assert.Equal(LinkOutcomeKind.Invalid, outcome.Kind);
		}
	}

	[Fact]
	public async Task ConsumeLinkCode_SecondConsume_IsInvalid()
	{
		const ulong userId = UserIdBase + 5;
		var web = await RegisterAsync("double-consume");
		await EnableDiscordUserAsync(fixture, userId, "discord-user");

		string code;
		await using (var db = fixture.CreateContext())
		{
			var service = CreateService(db);
			var link = await service.CreateLinkCodeAsync(web.AccountId);
			Assert.NotNull(link);
			code = link!.Value.Code;
			Assert.Equal(LinkOutcomeKind.Merged,
			             (await service.ConsumeLinkCodeAsync(code, userId, "discord-user")).Kind);
		}

		await using (var db = fixture.CreateContext())
		{
			var outcome = await CreateService(db).ConsumeLinkCodeAsync(code, userId, "discord-user");
			Assert.False(outcome.IsSuccess);
			Assert.Equal(LinkOutcomeKind.Invalid, outcome.Kind); // same generic message, no "already used" leak
		}
	}

	[Fact]
	public async Task ConsumeLinkCode_UnknownDiscordUser_IsNotEnabled()
	{
		var web = await RegisterAsync("not-enabled");

		string code;
		await using (var db = fixture.CreateContext())
		{
			var link = await CreateService(db).CreateLinkCodeAsync(web.AccountId);
			Assert.NotNull(link);
			code = link!.Value.Code;
		}

		await using (var db = fixture.CreateContext())
		{
			var outcome = await CreateService(db).ConsumeLinkCodeAsync(code, UserIdBase + 6, "never-seen");
			Assert.False(outcome.IsSuccess);
			Assert.Equal(LinkOutcomeKind.NotEnabled, outcome.Kind);
		}
	}

	[Fact]
	public async Task ConsumeLinkCode_NotEnabled_DoesNotBurnTheCode()
	{
		var web = await RegisterAsync("code-survives-notenabled");
		var discordUserId = UserIdBase + 7;
		await EnableDiscordUserAsync(fixture, discordUserId, "late-enabler");

		string code;
		await using (var db = fixture.CreateContext())
		{
			var link = await CreateService(db).CreateLinkCodeAsync(web.AccountId);
			Assert.NotNull(link);
			code = link!.Value.Code;
		}

		// first attempt with an unknown snowflake: NotEnabled, and the code must survive
		await using (var db = fixture.CreateContext())
		{
			var outcome = await CreateService(db).ConsumeLinkCodeAsync(code, UserIdBase + 8, "unknown");
			Assert.Equal(LinkOutcomeKind.NotEnabled, outcome.Kind);
		}

		// the same code still works for a user who IS enabled
		await using (var db = fixture.CreateContext())
		{
			var outcome = await CreateService(db).ConsumeLinkCodeAsync(code, discordUserId, "late-enabler");
			Assert.True(outcome.IsSuccess);
		}
	}

	[Fact]
	public async Task CreateLinkCode_ReplacesPriorUnconsumedCodes()
	{
		var web = await RegisterAsync("code-rotation");

		await using var db = fixture.CreateContext();
		var service = CreateService(db);
		var first = await service.CreateLinkCodeAsync(web.AccountId);
		var second = await service.CreateLinkCodeAsync(web.AccountId);

		Assert.NotNull(first);
		Assert.NotNull(second);
		Assert.False(await db.LinkCodes.AnyAsync(l => l.Code == first!.Value.Code)); // prior code invalidated
		Assert.True(await db.LinkCodes.AnyAsync(l => l.Code == second!.Value.Code));
	}

	[Fact]
	public async Task UnlinkDiscord_DeletesOnlyTheUserRow()
	{
		const ulong userId = UserIdBase + 7;
		const ulong gameId = GameIdBase + 6;
		var web = await RegisterAsync("unlink");
		await SeedGameAsync(fixture, gameId);
		await SeedWatchlistAsync(fixture, web.AccountId, gameId);
		await SeedDiscordUserAsync(fixture, web.AccountId, userId);

		await using (var db = fixture.CreateContext())
		{
			Assert.True(await CreateService(db).UnlinkDiscordAsync(web.AccountId));
		}

		await using var verify = fixture.CreateContext();
		Assert.False(await verify.Users.AnyAsync(u => u.UserId == userId));
		Assert.True(await verify.Accounts.AnyAsync(a => a.AccountId == web.AccountId));
		Assert.True(await verify.WebAccounts.AnyAsync(w => w.AccountId == web.AccountId));
		Assert.True(await verify.Watchlist.AnyAsync(w => w.AccountId == web.AccountId && w.GameId == gameId));

		await using var db2 = fixture.CreateContext();
		Assert.False(await CreateService(db2).UnlinkDiscordAsync(web.AccountId)); // nothing left to unlink
	}

	[Fact]
	public async Task GetWatchedGames_OrdersByLastUpdatedDescending()
	{
		const ulong oldest = GameIdBase + 7;
		const ulong newest = GameIdBase + 8;
		const ulong middle = GameIdBase + 9;
		var web = await RegisterAsync("ordered");
		await SeedGameAsync(fixture, oldest, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		await SeedGameAsync(fixture, newest, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		await SeedGameAsync(fixture, middle, new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
		await SeedWatchlistAsync(fixture, web.AccountId, oldest, newest, middle);

		await using var db = fixture.CreateContext();
		var games = await WebEndpointExtensions.GetWatchedGamesAsync(db, web.AccountId);

		Assert.Equal([newest, middle, oldest], games.Select(g => g.GameId).ToList());
		var first = games[0];
		Assert.Equal($"Game {newest}", first.Title);
		Assert.Equal($"https://f95zone.to/threads/{newest}", first.Url);
		Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), first.LastUpdated);
	}
}
