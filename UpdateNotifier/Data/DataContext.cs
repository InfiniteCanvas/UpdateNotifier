using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Data;

public sealed class DataContext(ILogger<DataContext> logger, Config config) : DbContext
{
	public DbSet<DiscordUser> Users     => Set<DiscordUser>();
	public DbSet<Game>        Games     => Set<Game>();
	public DbSet<Watchlist>   Watchlist => Set<Watchlist>();

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		optionsBuilder.UseSqlite($"Data Source={config.DatabasePath}");
		logger.ZLogInformation($"Database path: {config.DatabasePath}");
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Watchlist>().HasKey(watchlist => new { watchlist.UserId, watchlist.GameId });
		modelBuilder.Entity<Watchlist>()
		            .HasOne<DiscordUser>()
		            .WithMany(user => user.Watchlist)
		            .HasForeignKey(watchlist => watchlist.UserId);
		modelBuilder.Entity<Watchlist>()
		            .HasOne<Game>()
		            .WithMany(game => game.Watchers)
		            .HasForeignKey(watchlist => watchlist.GameId);
	}
}