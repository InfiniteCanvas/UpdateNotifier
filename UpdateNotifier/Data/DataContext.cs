using System.Text;
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
	public DbSet<User>           Users     => Set<User>();
	public DbSet<Game>           Games     => Set<Game>();
	public DbSet<WatchlistEntry> Watchlist => Set<WatchlistEntry>();

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		optionsBuilder.UseSqlite($"Data Source={config.DatabasePath}").AddInterceptors(new HashInterceptor());
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<WatchlistEntry>().HasKey(watchlist => new { watchlist.UserId, watchlist.GameId });
		modelBuilder.Entity<User>()
		            .HasMany(u => u.Games)
		            .WithMany(g => g.Watchers)
		            .UsingEntity<WatchlistEntry>(builder => builder.HasOne(w => w.Game).WithMany(),
		                                         builder => builder.HasOne(w => w.User).WithMany().OnDelete(DeleteBehavior.Cascade));
		modelBuilder.Entity<User>()
		            .Property(u => u.Hash)
		            .HasComputedColumnSql("user_hash(cast(UserId as text))", true)
		            .IsRequired();
	}

	public bool UserExists(ulong userId) => Users.Any(u => u.UserId == userId);

	public bool AddUser(ulong userId)
	{
		try
		{
			if (UserExists(userId))
			{
				logger.ZLogTrace($"User {userId} already exists in database");
				return true;
			}

			var user = new User(userId);

			Users.Add(user);
			SaveChanges();

			logger.ZLogInformation($"User {userId} has been added to database");
			return true;
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"Error adding user {userId} to database");
			return false;
		}
	}

	public bool RemoveUser(ulong userId)
	{
		try
		{
			var user = Users.FirstOrDefault(u => u.UserId == userId);
			if (user == null)
			{
				logger.ZLogTrace($"User {userId} does not exist in database");
				// return true here since user deletion was the goal and user does not exist
				return true;
			}

			Users.Remove(user);
			SaveChangesAsync();
			logger.ZLogInformation($"User {userId} has been removed from database");
			return true;
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"Error removing user {userId} from database");
			return false;
		}
	}


	public async Task<(bool success, string response)> AddGames(ulong userId, bool privileged, string[] urls, CancellationToken ct = default)
	{
		var userExists = await Users.AnyAsync(u => u.UserId == userId, ct);
		if (!userExists)
		{
			logger.ZLogError($"User {userId} does not exist, aborting adding to watchlist.");
			return (false, "User not found. Use /enable first.");
		}

		var gamesCount = await Watchlist.CountAsync(w => w.UserId == userId, ct);
		if (!privileged && gamesCount + urls.Length >= 420)
			return (false, $"You have {gamesCount} games  tracked and want to add {urls.Length} to the watchlist\n"
			             + $"Wanna keep track of more than 420 games? Why do you even keep track of that many?\n"
			             + "Support me on patreon (or wherever I setup, idk)!\n"
			             + "Or self-host an instance - https://github.com/InfiniteCanvas/UpdateNotifier");

		var sanitizedUrls = urls.Select(url => url.GetSanitizedUrl(out var sanitizedUrl) ? sanitizedUrl : string.Empty)
		                        .Where(s => !string.IsNullOrEmpty(s));
		var valid = new List<string>();
		var invalid = new List<string>();
		var errors = new List<string>();
		foreach (var url in sanitizedUrls)
		{
			if (!url.GetThreadId(out var threadId))
			{
				logger.ZLogWarning($"Thread {threadId} is malformed, cannot parse.");
				continue;
			}

			await using var transaction = await Database.BeginTransactionAsync(ct);
			try
			{
				var exists = await Games.AnyAsync(g => g.GameId == threadId, ct);

				if (!exists)
				{
					var gameInfo = await gameInfoProvider.GetGameInfo(url);
					await Games.AddAsync(gameInfo, ct);
					await SaveChangesAsync(ct);
				}

				// I should change to use Watchlist instead of changing the user's Games list
				// the nav prop is not tracked, so it's causing problems
				if (!await Watchlist.AnyAsync(w => w.UserId == userId && w.GameId == threadId, ct))
				{
					Watchlist.Add(new WatchlistEntry { UserId = userId, GameId = threadId });
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

		var builder = new StringBuilder();
		if (errors.Any())
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

		return errors.Any() ? (false, builder.ToString()) : (true, builder.ToString());
	}
}