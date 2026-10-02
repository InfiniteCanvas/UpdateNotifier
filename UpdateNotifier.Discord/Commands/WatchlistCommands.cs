using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;
using DiscordPrivilegeChecker = UpdateNotifier.Bot.DiscordPrivilegeChecker;
using ZLogger;

// ReSharper disable UnusedMember.Global

namespace UpdateNotifier.Commands;

public class WatchlistCommands(ILogger<WatchlistCommands> logger, UpdateNotifierApiClient api, IHttpClientFactory httpClientFactory, DiscordPrivilegeChecker privilegeChecker)
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

		try
		{
			var (_, response) = await api.WatchAsync(user.Id, urls, privilegeChecker.IsPrivileged(user));
			await RespondAsync(response, ephemeral: true);
		}
		catch (UserNotFoundException e)
		{
			await RespondAsync(e.Message, ephemeral: true);
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"[{e.GetType()}]Failed to add games {urlsCombined}");
			await RespondAsync($"Something went wrong trying to add:\n{urlsCombined}", ephemeral: true);
		}
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

		try
		{
			var client = httpClientFactory.CreateClient();
			var urlsCombined = await client.GetStringAsync(attachment.Url);
			var urls = urlsCombined.Split('\n');

			var (_, response) = await api.WatchAsync(user.Id, urls, privilegeChecker.IsPrivileged(user));
			await RespondAsync(response, ephemeral: true);
		}
		catch (UserNotFoundException e)
		{
			await RespondAsync(e.Message, ephemeral: true);
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"[{e.GetType()}]Failed to import watchlist from {attachment.Url}");
			await RespondAsync("Something went wrong while importing the watchlist.", ephemeral: true);
		}
	}

	[SlashCommand("unwatch", "Remove threads from the watchlist."), Alias("remove")]
	public async Task RemoveFromWatchlist([Discord.Interactions.Summary(description: "Space-separated list of URLs")] string urlsCombined)
	{
		var urls = urlsCombined.Split(' ');
		var user = Context.User;
		try
		{
			var privileged = user is SocketGuildUser guildUser && privilegeChecker.IsPrivileged(guildUser);
			var (_, response) = await api.UnwatchAsync(user.Id, urls, privileged);
			await RespondAsync(response, ephemeral: true);
		}
		catch (UserNotFoundException e)
		{
			logger.ZLogError($"User {user.Id} does not exist, aborting removing from watchlist.");
			await RespondAsync(e.Message, ephemeral: true);
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"[{e.GetType()}]Failed to remove games {urlsCombined}");
			await RespondAsync($"Something went wrong trying to remove:\n{urlsCombined}", ephemeral: true);
		}
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

		try
		{
			var games = await api.GetWatchlistAsync(user.Id);

			if (games.Count > 0)
			{
				var gameInfos = (includeTitle, includeUrl) switch
				{
					(true, true)  => games.Select(g => $"{g.Title} - {g.Url}"),
					(false, true) => games.Select(g => g.Url),
					_             => games.Select(g => g.Title),
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
		catch (UserNotFoundException e)
		{
			logger.ZLogError($"User {user.Id} does not exist, aborting listing.");
			await RespondAsync(e.Message, ephemeral: true);
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"[{e.GetType()}]Failed to list watchlist for {user.Id}");
			await RespondAsync("Something went wrong while getting your watchlist.", ephemeral: true);
		}
	}
}
