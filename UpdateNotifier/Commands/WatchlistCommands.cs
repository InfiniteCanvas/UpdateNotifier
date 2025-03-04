using System.Text;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Commands;

public class WatchlistCommands(ILogger<WatchlistCommands> logger, DataContext db, IHttpClientFactory httpClientFactory)
	: InteractionModuleBase<SocketInteractionContext>
{
	[SlashCommand("watch", "Watch a thread and get updates from it."), Alias("add")]
	public async Task AddToWatchlist([Discord.Interactions.Summary(description: "Space-separated list of URLs like this: '/watch url1 url2'")] string urlsCombined)
	{
		var urls = urlsCombined.Split(' ');
		if (Context.User is not SocketGuildUser user)
		{
			logger.ZLogError($"User is not a SocketGuildUser.");
			await RespondAsync("Something went wrong.", ephemeral: true);
			return;
		}

		var (_, response) = await db.AddGames(user.Id, user.IsPrivileged(), urls);
		await RespondAsync(response, ephemeral: true);
	}

	[SlashCommand("import_watchlist", "Watch a thread and get updates from it.")]
	public async Task ImportWatchlist(IAttachment attachment)
	{
		if (!attachment.ContentType.Contains("text/plain"))
		{
			logger.ZLogError($"Attachment is {attachment.ContentType}.");
			await RespondAsync("Attached file is not a text file.", ephemeral: true);
			return;
		}

		if (Context.User is not SocketGuildUser user)
		{
			logger.ZLogError($"User is not a SocketGuildUser.");
			await RespondAsync("Something went wrong.", ephemeral: true);
			return;
		}

		var client = httpClientFactory.CreateClient();
		var urlsCombined = await client.GetStringAsync(attachment.Url);
		var urls = urlsCombined.Split('\n');

		var (_, response) = await db.AddGames(user.Id, user.IsPrivileged(), urls);
		await RespondAsync(response, ephemeral: true);
	}

	[SlashCommand("unwatch", "Remove threads from the watchlist."), Alias("remove")]
	public async Task RemoveFromWatchlist([Discord.Interactions.Summary(description: "Space-separated list of URLs")] string urlsCombined)
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

	[SlashCommand("list", "Returns the watchlist. Sends a file if you're watching tons of threads.")]
	public async Task GetWatchlist([Discord.Interactions.Summary(description: "Include the game's title?")] bool includeTitle = true,
	                               [Discord.Interactions.Summary(description: "Include the game's url?")]
	                               bool includeUrl = true)
	{
		if (!includeTitle && !includeUrl)
		{
			await RespondAsync("Nothing to return. :kek:", ephemeral: true);
			return;
		}

		var user = Context.User;
		var dbUser = db.Users.Include(u => u.Games).FirstOrDefault(u => u.UserId == user.Id);
		if (dbUser == null)
		{
			logger.ZLogError($"User {user.Id} does not exist, aborting listing.");
			await RespondAsync("User not found. Use /enable first.", ephemeral: true);
			return;
		}

		var orderedGames = dbUser.Games.OrderByDescending(game => game).ToList();
		if (orderedGames.Any())
		{
			var gameInfos = (includeTitle, includeUrl) switch
			{
				(true, true)  => orderedGames.Select(g => $"{g.Title} - {g.Url}"),
				(false, true) => orderedGames.Select(g => g.Url),
				_             => orderedGames.Select(g => g.ToString()),
			};
			var allGames = string.Join("\n", gameInfos);
			if (allGames.Length > 2000) await RespondWithFileAsync(allGames.StringToStream(), "Watchlist.txt", "Too many games to list in a message.", ephemeral: true);
			else await RespondAsync(allGames, ephemeral: true);
		}
		else
		{
			await RespondAsync("Empty watchlist :(", ephemeral: true);
		}
	}
}