using System.Text;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data;
using UpdateNotifier.Utilities;
using ZLogger;
using Game = UpdateNotifier.Data.Models.Game;

namespace UpdateNotifier.Commands;

public class WatchlistCommands(ILogger<WatchlistCommands> logger, DataContext db)
	: InteractionModuleBase<SocketInteractionContext>
{
	[SlashCommand("watch", "Watch a thread and get updates from it."), Alias("add")]
	public async Task AddToWatchlist([Discord.Interactions.Summary(description: "Space-separated list of RSS feed URLs")] string urlsCombined)
	{
		var urls = urlsCombined.Split(' ');
		var user = Context.User;
		var dbUser = db.Users.Include(u => u.Games).FirstOrDefault(u => u.UserId == user.Id);
		if (dbUser == null)
		{
			logger.ZLogError($"User {user.Id} does not exist, aborting adding to watchlist.");
			await RespondAsync("User not found. Use /enable first.", ephemeral: true);
			return;
		}

		var sanitizedUrls = urls.Select(url => url.GetSanitizedUrl(out var sanitizedUrl) ? sanitizedUrl : string.Empty)
		                        .Where(s => !string.IsNullOrEmpty(s));
		var valid = new List<string>();
		var invalid = new List<string>();
		foreach (var url in sanitizedUrls)
		{
			if (!url.GetThreadId(out var threadId))
			{
				logger.ZLogWarning($"Thread {threadId} is malformed, cannot parse.");
				continue;
			}

			var game = dbUser.Games.Find(g => g.GameId == threadId);
			if (game != null)
			{
				invalid.Add(url);
			}
			else
			{
				game = await db.Games.FindAsync(threadId) ?? new Game();
				dbUser.Games.Add(game);
				valid.Add(url);
				// db.Watchlist.Add(new WatchlistEntry { GameId = game.GameId, UserId = user.Id });
				logger.ZLogDebug($"Added {url} to watchlist of user {user.Id}");
			}
		}

		await db.SaveChangesAsync();

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

		await RespondAsync(builder.ToString(), ephemeral: true);
	}

	[SlashCommand("unwatch", "Remove threads from the watchlist."), Alias("remove")]
	public async Task RemoveFromWatchlist([Discord.Interactions.Summary(description: "Space-separated list of RSS feed URLs")] string urlsCombined)
	{
		var urls = urlsCombined.Split(' ');
		var user = Context.User;
		var dbUser = db.Users.Include(u => u.Games).FirstOrDefault(u => u.UserId == user.Id);
		if (dbUser == null)
		{
			logger.ZLogError($"User {user.Id} does not exist, aborting adding to watchlist.");
			await RespondAsync("User not found. Use /enable first.", ephemeral: true);
			return;
		}

		var sanitizedUrls = urls.Select(url => url.GetSanitizedUrl(out var sanitizedUrl) ? sanitizedUrl : string.Empty)
		                        .Where(s => !string.IsNullOrEmpty(s));
		var valid = new List<string>();
		var invalid = new List<string>();
		foreach (var url in sanitizedUrls)
		{
			if (!url.GetThreadId(out var threadId))
			{
				logger.ZLogWarning($"Thread {threadId} is malformed, cannot parse.");
				continue;
			}

			var game = dbUser.Games.Find(g => g.GameId == threadId);
			if (game == null)
			{
				invalid.Add(url);
			}
			else
			{
				valid.Add(url);
				dbUser.Games.Remove(game);
				logger.ZLogDebug($"Removed {url} to watchlist of user {user.Id}");
			}
		}

		await db.SaveChangesAsync();

		var builder = new StringBuilder();
		if (valid.Count > 0)
		{
			builder.Append("Games removed: ");
			builder.AppendJoin(" ", valid);
			builder.AppendLine();
		}

		if (invalid.Count > 0)
		{
			builder.Append("Games didn't exist in watchlist: ");
			builder.AppendJoin(" ", invalid);
		}

		await RespondAsync(builder.ToString(), ephemeral: true);
	}

	[SlashCommand("list", "Returns the watchlist as a text file (it can get huge).")]
	public async Task GetWatchlist()
	{
		var user = Context.User;
		var dbUser = db.Users.Include(u => u.Games).FirstOrDefault(u => u.UserId == user.Id);
		if (dbUser == null)
		{
			logger.ZLogError($"User {user.Id} does not exist, aborting listing.");
			await RespondAsync("User not found. Use /enable first.", ephemeral: true);
			return;
		}

		var orderedGames = dbUser.Games.OrderByDescending(game => game).ToList();
		var allGames = string.Join("\n", orderedGames);
		if (orderedGames.Any())
		{
			await user.SendFileAsync(allGames.StringToStream(), "Watchlist.txt");
			await RespondAsync("Sending watchlist...", ephemeral: true);
		}
		else
		{
			await RespondAsync("Empty watchlist :(", ephemeral: true);
		}
	}
}