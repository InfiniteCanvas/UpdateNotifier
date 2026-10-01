using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data.Functions;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Data;

public sealed class DataContext(ILogger<DataContext> logger, Config config, GameInfoProvider gameInfoProvider) : DbContext
{
	/// <summary>
	///     Serializes watchlist mutations. Static because the context itself is transient -
	///     the lock must hold across instances, and it will also guard the later link-merge flow.
	/// </summary>
	private static readonly SemaphoreSlim MutationLock = new(1, 1);

	public DbSet<User>           Users       => Set<User>();
	public DbSet<Account>        Accounts    => Set<Account>();
	public DbSet<Game>           Games       => Set<Game>();
	public DbSet<WatchlistEntry> Watchlist   => Set<WatchlistEntry>();
	public DbSet<WebAccount>     WebAccounts => Set<WebAccount>();
	public DbSet<WebSession>     WebSessions => Set<WebSession>();
	public DbSet<LinkCode>       LinkCodes   => Set<LinkCode>();

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		=> optionsBuilder.UseSqlite($"Data Source={config.DatabasePath}").AddInterceptors(new HashInterceptor());

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<WatchlistEntry>().HasKey(watchlist => new { watchlist.AccountId, watchlist.GameId });
		modelBuilder.Entity<Account>()
		            .HasMany(a => a.Games)
		            .WithMany(g => g.Watchers)
		            .UsingEntity<WatchlistEntry>(builder => builder.HasOne(w => w.Game).WithMany(),
		                                         builder => builder.HasOne(w => w.Account).WithMany().OnDelete(DeleteBehavior.Cascade));
		modelBuilder.Entity<Account>()
		            .Property(a => a.Hash)
		            .HasMaxLength(40)
		            .IsRequired();
		modelBuilder.Entity<Account>()
		            .HasIndex(a => a.Hash)
		            .IsUnique();
		modelBuilder.Entity<User>()
		            .HasOne(u => u.Account)
		            .WithOne(a => a.User)
		            .HasForeignKey<User>(u => u.AccountId)
		            .IsRequired()
		            .OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<User>()
		            .HasIndex(u => u.AccountId)
		            .IsUnique();
		modelBuilder.Entity<WebAccount>()
		            .HasOne(w => w.Account)
		            .WithMany()
		            .HasForeignKey(w => w.AccountId)
		            .IsRequired()
		            .OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<WebAccount>()
		            .HasIndex(w => w.AccountId)
		            .IsUnique();
		modelBuilder.Entity<WebAccount>()
		            .HasIndex(w => w.Username)
		            .IsUnique();
		modelBuilder.Entity<WebAccount>()
		            .Property(w => w.Username)
		            .UseCollation("NOCASE");
		modelBuilder.Entity<WebSession>()
		            .HasOne(s => s.Account)
		            .WithMany()
		            .HasForeignKey(s => s.AccountId)
		            .IsRequired()
		            .OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<LinkCode>()
		            .HasOne(l => l.Account)
		            .WithMany()
		            .HasForeignKey(l => l.AccountId)
		            .IsRequired()
		            .OnDelete(DeleteBehavior.Cascade);
	}

	public bool UserExists(ulong userId) => Users.Any(u => u.UserId == userId);

	public async Task<Account?> GetAccountByHashAsync(string hash, CancellationToken ct = default)
	{
		// hashes are stored uppercase; an exact SQL match needs an uppercased input
		// (EF cannot translate the StringComparison overloads)
		var normalized = hash.Trim().ToUpperInvariant();
		return await Accounts.Include(a => a.User)
		                     .FirstOrDefaultAsync(a => a.Hash == normalized, ct);
	}

	public bool AddUser(ulong userId, string? discordUsername)
	{
		try
		{
			if (UserExists(userId))
			{
				logger.ZLogTrace($"User {userId} already exists in database");
				return true;
			}

			var account = new Account
			{
				Hash = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)),
				CreatedAt = DateTime.UtcNow
			};
			Accounts.Add(account);

			// linked through the navigation (not the temporary AccountId): a single SaveChanges
			// inserts the account, propagates its generated key into the FK and inserts the user
			var user = new User(userId) { Account = account, DiscordUsername = discordUsername };
			Users.Add(user);
			SaveChanges();

			logger.ZLogInformation($"User {userId} has been added to database");
			return true;
		}
		catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 } sqliteException
		                                  && sqliteException.Message.Contains("Users.UserId"))
		{
			// raced a concurrent AddUser for the same snowflake; the user is enabled either way
			logger.ZLogWarning($"User {userId} already enabled: {sqliteException.Message}");
			return true;
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"Error adding user {userId} to database");
			return false;
		}
	}

	public async Task<bool> RemoveUser(ulong userId)
	{
		try
		{
			var user = await Users.FirstOrDefaultAsync(u => u.UserId == userId);
			if (user == null)
			{
				logger.ZLogTrace($"User {userId} does not exist in database");
				// return true here since user deletion was the goal and user does not exist
				return true;
			}

			// removing the account cascades the watchlist, the linked web rows and the user row itself
			var account = await Accounts.FirstOrDefaultAsync(a => a.AccountId == user.AccountId);
			if (account != null)
				Accounts.Remove(account);
			else
				Users.Remove(user);

			await SaveChangesAsync();
			logger.ZLogInformation($"User {userId} has been removed from database");
			return true;
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"Error removing user {userId} from database");
			return false;
		}
	}


	public async Task<(bool success, string response)> TrackGames(string hash, string[] urls, bool privileged, CancellationToken ct = default)
	{
		var account = await GetAccountByHashAsync(hash, ct);
		if (account == null)
		{
			logger.ZLogError($"User {hash.RedactHash()} does not exist, aborting adding to watchlist.");
			return (false, "User was not found");
		}

		var gamesCount = await Watchlist.CountAsync(w => w.AccountId == account.AccountId, ct);
		if (!privileged && gamesCount + urls.Length >= Config.FREE_USER_LIMIT)
			return (false, $"You have {gamesCount} games  tracked and want to add {urls.Length} to the watchlist\n"
			             + $"Wanna keep track of more than {Config.FREE_USER_LIMIT} games?\n"
			             + "Support me on patreon here: patreon.com/F95UpdateNotifier \n"
			             + "Or self-host an instance - https://github.com/InfiniteCanvas/UpdateNotifier");

		var sanitizedUrls = urls.Select(url => url.GetSanitizedUrl(out var sanitizedUrl) ? sanitizedUrl : string.Empty)
		                        .Where(s => !string.IsNullOrEmpty(s));
		var valid = new List<string>();
		var invalid = new List<string>();
		var errors = new List<string>();

		await MutationLock.WaitAsync(ct);
		try
		{
			foreach (var url in sanitizedUrls)
			{
				if (!url.GetThreadId(out var threadId))
				{
					logger.ZLogWarning($"Thread {threadId} is malformed, cannot parse.");
					continue;
				}

				// scrape BEFORE opening the transaction: network I/O must never hold a SQLite write lock
				Game? gameInfo = null;
				if (!await Games.AnyAsync(g => g.GameId == threadId, ct))
					gameInfo = await gameInfoProvider.GetGameInfo(url);

				await using var transaction = await Database.BeginTransactionAsync(ct);
				try
				{
					if (gameInfo != null)
					{
						await Games.AddAsync(gameInfo, ct);
						await SaveChangesAsync(ct);
					}

					if (!await Watchlist.AnyAsync(w => w.AccountId == account.AccountId && w.GameId == threadId, ct))
					{
						Watchlist.Add(new WatchlistEntry { AccountId = account.AccountId, GameId = threadId });
						await SaveChangesAsync(ct);
						valid.Add(url);
					}
					else
					{
						invalid.Add(url);
					}

					await transaction.CommitAsync(ct);
				}
				catch (DbUpdateConcurrencyException e)
				{
					await transaction.RollbackAsync(ct);
					logger.ZLogError(e, $"Error adding game {url} to watchlist");
					errors.Add(url);
				}
			}
		}
		finally
		{
			MutationLock.Release();
		}

		var builder = new StringBuilder();
		if (errors.Count != 0)
		{
			builder.AppendLine("There were errors during adding to watchlist:");
			builder.AppendJoin("\n", errors);
		}

		if (valid.Count > 0)
		{
			builder.Append("Games added: ");
			builder.AppendJoin(" ", valid);
			builder.AppendLine();
		}

		if (invalid.Count > 0)
		{
			builder.Append("Games already in watchlist: ");
			builder.AppendJoin(" ", invalid);
		}

		return errors.Count != 0 ? (false, builder.ToString()) : (true, builder.ToString());
	}

	public async Task<(bool success, string response)> UntrackGames(string hash, string[] urls, bool privileged, CancellationToken ct = default)
	{
		var account = await GetAccountByHashAsync(hash, ct);
		if (account == null)
		{
			logger.ZLogError($"User {hash.RedactHash()} does not exist, aborting removing from watchlist.");
			return (false, "User was not found");
		}

		await MutationLock.WaitAsync(ct);
		try
		{
			var sanitizedUrls = urls.Select(url => url.GetSanitizedUrl(out var sanitizedUrl) ? sanitizedUrl : string.Empty)
			                        .Where(s => !string.IsNullOrEmpty(s))
			                        .ToImmutableArray();
			var threadIds = sanitizedUrls.Where(url => url.GetThreadId(out _))
			                             .Select(url =>
			                                     {
				                                     url.GetThreadId(out var threadId);
				                                     return threadId;
			                                     })
			                             .ToImmutableArray();
			var watchlistEntries = Watchlist.Where(entry => entry.AccountId == account.AccountId && threadIds.Contains(entry.GameId));

			if (!watchlistEntries.Any()) return (true, "No games on the watchlist found to remove.");

			await using var transaction = await Database.BeginTransactionAsync(ct);

			Watchlist.RemoveRange(watchlistEntries);
			await SaveChangesAsync(ct);
			await transaction.CommitAsync(ct);

			return (true, $"Successfully removed these games from watchlist: {string.Join(' ', sanitizedUrls)}");
		}
		finally
		{
			MutationLock.Release();
		}
	}
}