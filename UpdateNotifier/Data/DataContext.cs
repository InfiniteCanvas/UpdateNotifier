using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data.Functions;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Data;

public sealed class DataContext : DbContext
{
	private readonly Config               _config;
	private readonly GameInfoProvider     _gameInfoProvider;
	private readonly ILogger<DataContext> _logger;

	public DataContext(ILogger<DataContext> logger, Config config, GameInfoProvider gameInfoProvider)
	{
		_logger = logger;
		_config = config;
		_gameInfoProvider = gameInfoProvider;
		Database.Migrate();
	}

	public DbSet<User>           Users     => Set<User>();
	public DbSet<Game>           Games     => Set<Game>();
	public DbSet<WatchlistEntry> Watchlist => Set<WatchlistEntry>();

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		=> optionsBuilder.UseSqlite($"Data Source={_config.DatabasePath}").AddInterceptors(new HashInterceptor());

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
				_logger.ZLogTrace($"User {userId} already exists in database");
				return true;
			}

			var user = new User(userId);

			Users.Add(user);
			SaveChanges();

			_logger.ZLogInformation($"User {userId} has been added to database");
			return true;
		}
		catch (Exception e)
		{
			_logger.ZLogError(e, $"Error adding user {userId} to database");
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
				_logger.ZLogTrace($"User {userId} does not exist in database");
				// return true here since user deletion was the goal and user does not exist
				return true;
			}

			Users.Remove(user);
			SaveChangesAsync();
			_logger.ZLogInformation($"User {userId} has been removed from database");
			return true;
		}
		catch (Exception e)
		{
			_logger.ZLogError(e, $"Error removing user {userId} from database");
			return false;
		}
	}


	public async Task<(bool success, string response)> AddGames(ulong userId, bool privileged, string[] urls)
	{
		var dbUser = Users.Include(u => u.Games).FirstOrDefault(u => u.UserId == userId);
		if (dbUser == null)
		{
			_logger.ZLogError($"User {userId} does not exist, aborting adding to watchlist.");
			return (false, "User not found. Use /enable first.");
		}

		if (!privileged && dbUser.Games.Count + urls.Length >= 420)
			return (false, $"You have {dbUser.Games.Count} games  tracked and want to add {urls.Length} to the watchlist\n"
			             + $"Wanna keep track of more than 420 games? Why do you even keep track of that many?\n"
			             + "Support me on patreon (or wherever I setup, idk)!\n"
			             + "Or self-host an instance - https://github.com/InfiniteCanvas/UpdateNotifier");

		var sanitizedUrls = urls.Select(url => url.GetSanitizedUrl(out var sanitizedUrl) ? sanitizedUrl : string.Empty)
		                        .Where(s => !string.IsNullOrEmpty(s));
		var valid = new List<string>();
		var invalid = new List<string>();
		foreach (var url in sanitizedUrls)
		{
			if (!url.GetThreadId(out var threadId))
			{
				_logger.ZLogWarning($"Thread {threadId} is malformed, cannot parse.");
				continue;
			}

			var game = dbUser.Games.Find(g => g.GameId == threadId);
			if (game != null)
			{
				invalid.Add(url);
			}
			else
			{
				game = await Games.FindAsync(threadId) ?? await _gameInfoProvider.GetGameInfo(url);
				dbUser.Games.Add(game);
				valid.Add(url);
				_logger.ZLogDebug($"Added {url} to watchlist of user {userId}");
			}
		}

		await SaveChangesAsync();

		var builder = new StringBuilder();
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

		var text = builder.ToString();

		return (true, text);
	}
}