using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Models;
using ZLogger;

namespace UpdateNotifier.Services;

/// <summary>Error classification for register/login; endpoints map these onto status codes.</summary>
public enum AuthError
{
	None = 0,
	InvalidInput,
	UsernameTaken,
	InvalidCredentials
}

/// <summary>
///     Register/login outcome; on success carries the raw session token - the only place the
///     unhashed token exists outside the browser cookie.
/// </summary>
public sealed record AuthResult(bool Success, AuthError Error, string Message, ulong AccountId, string Username, string Token, DateTime ExpiresAt)
{
	public static AuthResult Failed(AuthError error, string message)
		=> new(false, error, message, 0, string.Empty, string.Empty, DateTime.MinValue);
}

/// <summary>What happened when a link code was consumed; kept serialization-friendly for the Discord /link command.</summary>
public enum LinkOutcomeKind
{
	/// <summary>The Discord user was attached to the web account.</summary>
	Linked,

	/// <summary>The Discord user's previous account was merged into the web account.</summary>
	Merged,

	/// <summary>The Discord user already points at the issuing account (idempotent success).</summary>
	AlreadyLinked,

	/// <summary>No Discord user exists for the snowflake - the bot requires /enable first.</summary>
	NotEnabled,

	/// <summary>Missing, expired or already-consumed code - deliberately indistinguishable.</summary>
	Invalid,

	/// <summary>The web account already has a different Discord user linked.</summary>
	Conflict
}

public sealed record LinkOutcome(LinkOutcomeKind Kind, string Message, int GamesMerged = 0)
{
	public bool IsSuccess => Kind is LinkOutcomeKind.Linked or LinkOutcomeKind.Merged or LinkOutcomeKind.AlreadyLinked;

	public static LinkOutcome Linked()
		=> new(LinkOutcomeKind.Linked, "Your Discord account is now linked to the web account.");

	public static LinkOutcome Merged(int gamesMerged)
		=> new(LinkOutcomeKind.Merged, $"Accounts linked and merged; {gamesMerged} game(s) were carried over.", gamesMerged);

	public static LinkOutcome AlreadyLinked()
		=> new(LinkOutcomeKind.AlreadyLinked, "This Discord account is already linked to that web account.");

	public static LinkOutcome NotEnabled()
		=> new(LinkOutcomeKind.NotEnabled, "Discord notifications are not enabled for your account - run /enable on Discord first.");

	public static LinkOutcome Invalid()
		=> new(LinkOutcomeKind.Invalid, "That code is invalid or expired.");

	public static LinkOutcome Conflict()
		=> new(LinkOutcomeKind.Conflict, "That account is already linked to another Discord user.");
}

public interface IWebAuthService
{
	public Task<AuthResult> RegisterAsync(string username, string password, CancellationToken ct = default);

	public Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default);

	public Task<WebSession?> ValidateSessionAsync(string token, CancellationToken ct = default);

	public Task LogoutAsync(string token, CancellationToken ct = default);

	public Task<bool> DeleteAccountAsync(ulong accountId, string password, CancellationToken ct = default);

	public Task<string> RegenerateHashAsync(ulong accountId, CancellationToken ct = default);

	public Task<(string Code, DateTime ExpiresAt)?> CreateLinkCodeAsync(ulong accountId, CancellationToken ct = default);

	public Task<LinkOutcome> ConsumeLinkCodeAsync(string code, ulong discordSnowflake, string discordUsername, CancellationToken ct = default);

	public Task<bool> UnlinkDiscordAsync(ulong accountId, CancellationToken ct = default);

	public Task<WebAccount?> GetAccountAsync(ulong accountId, CancellationToken ct = default);
}

public sealed partial class WebAuthService(DataContext db, ILogger<WebAuthService> logger) : IWebAuthService
{
	private const int SessionLifetimeDays = 30;
	private const int LinkCodeLifetimeMinutes = 15;

	private const string InvalidCredentialsMessage = "Invalid username or password.";

	/// <summary>PBKDF2 (HMACSHA256) with a hardened iteration count - the only Identity piece in use.</summary>
	private static readonly PasswordHasher<Account> PasswordHasher =
		new(new OptionsWrapper<PasswordHasherOptions>(new PasswordHasherOptions { IterationCount = 210_000 }));

	[GeneratedRegex("^[A-Za-z0-9_.-]+$")]
	private static partial Regex UsernameRegex();

	public async Task<AuthResult> RegisterAsync(string username, string password, CancellationToken ct = default)
	{
		if (string.IsNullOrEmpty(username) || username.Length is < 3 or > 32 || !UsernameRegex().IsMatch(username))
			return AuthResult.Failed(AuthError.InvalidInput, "Username must be 3-32 characters using only letters, digits, '.', '_' or '-'.");

		if (string.IsNullOrEmpty(password) || password.Length < 8)
			return AuthResult.Failed(AuthError.InvalidInput, "Password must be at least 8 characters.");

		var account = new Account
		{
			Hash = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)),
			CreatedAt = DateTime.UtcNow
		};
		var webAccount = new WebAccount
		{
			Username = username,
			PasswordHash = PasswordHasher.HashPassword(account, password),
			Account = account // linked through the navigation: one SaveChanges inserts both rows
		};
		db.WebAccounts.Add(webAccount);

		try
		{
			await db.SaveChangesAsync(ct);
		}
		catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 })
		{
			// the NOCASE-unique index on WebAccounts.Username makes this the "taken" case
			logger.ZLogWarning($"Registration for {username} rejected: username already taken");
			return AuthResult.Failed(AuthError.UsernameTaken, "That username is already taken.");
		}

		var (token, expiresAt) = await CreateSessionAsync(account.AccountId, ct);
		logger.ZLogInformation($"Registered web account {username} (account {account.AccountId})");
		return new AuthResult(true, AuthError.None, string.Empty, account.AccountId, username, token, expiresAt);
	}

	public async Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default)
	{
		// case-insensitivity comes from the NOCASE collation on the Username column - a plain == translates fine
		var webAccount = await db.WebAccounts.Include(w => w.Account)
		                       .FirstOrDefaultAsync(w => w.Username == (username ?? string.Empty), ct);

		if (webAccount?.Account is null)
		{
			logger.ZLogWarning($"Login failed: unknown username");
			return AuthResult.Failed(AuthError.InvalidCredentials, InvalidCredentialsMessage);
		}

		var verification = PasswordHasher.VerifyHashedPassword(webAccount.Account, webAccount.PasswordHash, password ?? string.Empty);
		if (verification == PasswordVerificationResult.Failed)
		{
			logger.ZLogWarning($"Login failed: wrong password for account {webAccount.AccountId}");
			return AuthResult.Failed(AuthError.InvalidCredentials, InvalidCredentialsMessage);
		}

		if (verification == PasswordVerificationResult.SuccessRehashNeeded)
		{
			webAccount.PasswordHash = PasswordHasher.HashPassword(webAccount.Account, password!);
			await db.SaveChangesAsync(ct);
		}

		var (token, expiresAt) = await CreateSessionAsync(webAccount.AccountId, ct);
		logger.ZLogDebug($"Login succeeded for account {webAccount.AccountId}");
		return new AuthResult(true, AuthError.None, string.Empty, webAccount.AccountId, webAccount.Username, token, expiresAt);
	}

	public async Task<WebSession?> ValidateSessionAsync(string token, CancellationToken ct = default)
	{
		if (string.IsNullOrEmpty(token)) return null;

		// opportunistic purge of expired sessions and link codes; runs on session ops only, no background timer
		await db.WebSessions.Where(s => s.ExpiresAt < DateTime.UtcNow).ExecuteDeleteAsync(ct);
		await db.LinkCodes.Where(l => l.ExpiresAt < DateTime.UtcNow).ExecuteDeleteAsync(ct);

		return await db.WebSessions.FirstOrDefaultAsync(s => s.TokenHash == HashToken(token) && s.ExpiresAt > DateTime.UtcNow, ct);
	}

	public async Task LogoutAsync(string token, CancellationToken ct = default)
	{
		if (string.IsNullOrEmpty(token)) return;

		await db.WebSessions.Where(s => s.TokenHash == HashToken(token)).ExecuteDeleteAsync(ct);
	}

	public async Task<bool> DeleteAccountAsync(ulong accountId, string password, CancellationToken ct = default)
	{
		var webAccount = await db.WebAccounts.Include(w => w.Account)
		                       .FirstOrDefaultAsync(w => w.AccountId == accountId, ct);
		if (webAccount?.Account is null)
		{
			logger.ZLogWarning($"Account deletion rejected: no web account for {accountId}");
			return false;
		}

		if (PasswordHasher.VerifyHashedPassword(webAccount.Account, webAccount.PasswordHash, password ?? string.Empty)
		    == PasswordVerificationResult.Failed)
			return false;

		// removing the account cascades the watchlist, sessions, link codes, the web row and any linked Discord user
		db.Accounts.Remove(webAccount.Account);
		await db.SaveChangesAsync(ct);
		logger.ZLogInformation($"Deleted account {accountId}");
		return true;
	}

	public async Task<string> RegenerateHashAsync(ulong accountId, CancellationToken ct = default)
	{
		var account = await db.Accounts.SingleAsync(a => a.AccountId == accountId, ct);
		account.Hash = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
		await db.SaveChangesAsync(ct);

		logger.ZLogInformation($"Regenerated hash for account {accountId}");
		return account.Hash;
	}

	public async Task<(string Code, DateTime ExpiresAt)?> CreateLinkCodeAsync(ulong accountId, CancellationToken ct = default)
	{
		// already linked - the caller maps this to a 409
		if (await db.Users.AnyAsync(u => u.AccountId == accountId, ct)) return null;

		// invalidate any prior unconsumed codes for this account
		await db.LinkCodes.Where(l => l.AccountId == accountId).ExecuteDeleteAsync(ct);

		var expiresAt = DateTime.UtcNow.AddMinutes(LinkCodeLifetimeMinutes);
		var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
		db.LinkCodes.Add(new LinkCode
		{
			Code = code,
			AccountId = accountId,
			CreatedAt = DateTime.UtcNow,
			ExpiresAt = expiresAt
		});
		await db.SaveChangesAsync(ct);

		logger.ZLogDebug($"Issued link code for account {accountId}");
		return (code, expiresAt);
	}

	public async Task<LinkOutcome> ConsumeLinkCodeAsync(string code, ulong discordSnowflake, string discordUsername, CancellationToken ct = default)
	{
		await DataContext.MutationLock.WaitAsync(ct);
		try
		{
			var now = DateTime.UtcNow;

			// resolve first (the account id is needed to act on it), then consume atomically:
			// the guarded delete makes a double consume impossible no matter the concurrency
			var targetAccountId = await db.LinkCodes
			                             .Where(l => l.Code == code && l.ExpiresAt > now)
			                             .Select(l => (ulong?) l.AccountId)
			                             .FirstOrDefaultAsync(ct);
			if (targetAccountId is not ulong targetId)
				return LinkOutcome.Invalid();

			var consumed = await db.LinkCodes.Where(l => l.Code == code && l.ExpiresAt > now).ExecuteDeleteAsync(ct);
			if (consumed != 1)
				return LinkOutcome.Invalid(); // same generic message as expired - never leak "already used"

			// the bot creates the user through /enable; linking cannot substitute for it
			var user = await db.Users.Include(u => u.Account)
			                  .FirstOrDefaultAsync(u => u.UserId == discordSnowflake, ct);
			if (user is null)
				return LinkOutcome.NotEnabled();

			if (user.AccountId == targetId)
				return LinkOutcome.AlreadyLinked();

			if (user.Account is null)
			{
				// a user row without its account is a corruption-only state (AddUser always creates both): re-link it
				user.Account = await db.Accounts.SingleAsync(a => a.AccountId == targetId, ct);
				user.DiscordUsername = discordUsername;
				await db.SaveChangesAsync(ct);
				return LinkOutcome.Linked();
			}

			return await MergeIntoTargetAccountAsync(user, targetId, discordUsername, ct);
		}
		finally
		{
			DataContext.MutationLock.Release();
		}
	}

	public async Task<bool> UnlinkDiscordAsync(ulong accountId, CancellationToken ct = default)
	{
		await DataContext.MutationLock.WaitAsync(ct);
		try
		{
			var user = await db.Users.FirstOrDefaultAsync(u => u.AccountId == accountId, ct);
			if (user is null) return false;

			// deletes ONLY the Discord link: the account, web login and watchlist survive;
			// re-linking requires /enable on the bot again
			db.Users.Remove(user);
			await db.SaveChangesAsync(ct);

			logger.ZLogInformation($"Unlinked Discord user {user.UserId} from account {accountId}");
			return true;
		}
		finally
		{
			DataContext.MutationLock.Release();
		}
	}

	public Task<WebAccount?> GetAccountAsync(ulong accountId, CancellationToken ct = default)
		=> db.WebAccounts.Include(w => w.Account).ThenInclude(a => a!.User)
		      .FirstOrDefaultAsync(w => w.AccountId == accountId, ct);

	/// <summary>
	///     Transfer-with-merge: the Discord user's watchlist is copied onto the web account, the user is
	///     re-pointed at it and the emptied source account is deleted - all in one atomic SaveChanges.
	/// </summary>
	private async Task<LinkOutcome> MergeIntoTargetAccountAsync(User user, ulong targetAccountId, string discordUsername, CancellationToken ct)
	{
		var sourceAccount = user.Account!;

		// clone the source watchlist onto the target for games the target does not track yet
		var sourceGames = await db.Watchlist.Where(w => w.AccountId == sourceAccount.AccountId)
		                         .Select(w => w.GameId)
		                         .ToListAsync(ct);
		var targetGames = await db.Watchlist.Where(w => w.AccountId == targetAccountId)
		                          .Select(w => w.GameId)
		                          .ToHashSetAsync(ct);

		var merged = 0;
		foreach (var gameId in sourceGames)
			if (targetGames.Add(gameId))
			{
				db.Watchlist.Add(new WatchlistEntry { AccountId = targetAccountId, GameId = gameId });
				merged++;
			}

		// order matters: re-point the user BEFORE removing the source account, so the client-side
		// cascade never marks the user for deletion; EF then emits the UPDATE before the DELETE,
		// keeping the row out of the database-side cascade as well
		user.AccountId = targetAccountId;
		user.DiscordUsername = discordUsername;
		db.Accounts.Remove(sourceAccount);

		try
		{
			await db.SaveChangesAsync(ct);
		}
		catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 })
		{
			// the unique index on Users.AccountId: the target already has a different Discord user
			logger.ZLogWarning($"Link conflict moving Discord user {user.UserId} onto account {targetAccountId}: {e.InnerException!.Message}");
			return LinkOutcome.Conflict();
		}

		logger.ZLogInformation($"Merged account {sourceAccount.AccountId} into {targetAccountId} for Discord user {user.UserId} ({merged} games carried over)");
		return LinkOutcome.Merged(merged);
	}

	/// <summary>Only the SHA-256 of the cookie token is stored; the raw value exists only in the result/cookie.</summary>
	private async Task<(string Token, DateTime ExpiresAt)> CreateSessionAsync(ulong accountId, CancellationToken ct)
	{
		var expiresAt = DateTime.UtcNow.AddDays(SessionLifetimeDays);
		var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
		db.WebSessions.Add(new WebSession
		{
			TokenHash = HashToken(token),
			AccountId = accountId,
			CreatedAt = DateTime.UtcNow,
			ExpiresAt = expiresAt
		});
		await db.SaveChangesAsync(ct);

		return (token, expiresAt);
	}

	private static string HashToken(string token)
		=> Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
