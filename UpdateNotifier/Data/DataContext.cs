using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Data;

public sealed class DataContext(ILogger<DataContext> logger, Config config) : DbContext
{
	public DbSet<User>           Users     => Set<User>();
	public DbSet<Game>           Games     => Set<Game>();
	public DbSet<WatchlistEntry> Watchlist => Set<WatchlistEntry>();

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		optionsBuilder.UseSqlite($"Data Source={config.DatabasePath}");
		logger.ZLogInformation($"Database path: {config.DatabasePath}");
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<WatchlistEntry>().HasKey(watchlist => new { watchlist.UserId, watchlist.GameId });
		modelBuilder.Entity<User>()
		            .HasMany(u => u.Games)
		            .WithMany(g => g.Watchers)
		            .UsingEntity<WatchlistEntry>(builder => builder.HasOne(w => w.Game).WithMany(),
		                                         builder => builder.HasOne(w => w.User).WithMany().OnDelete(DeleteBehavior.Cascade));
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

			var user = new User { UserId = userId };

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
}