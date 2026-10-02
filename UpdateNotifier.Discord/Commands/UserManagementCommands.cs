using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Services;
using ZLogger;

namespace UpdateNotifier.Commands;

public sealed class UserManagementCommands(ILogger<UserManagementCommands> logger, UpdateNotifierApiClient api)
	: InteractionModuleBase<SocketInteractionContext>
{
	[SlashCommand("disable", "Disable the bot's functions and delete all user data.")]
	public async Task DisableBot()
	{
		var userId = Context.User.Id;
		var user = await api.GetUserAsync(userId);
		if (!user.Exists)
		{
			await RespondAsync("This user doesn't exist.", ephemeral: true);
			return;
		}

		var embed = new EmbedBuilder()
		           .WithTitle("Are you sure?")
		           .WithDescription("By disabling this bot's service, the following will happen:\n\n"
		                          + "1. The watchlist associated with your user id will be deleted.\n"
		                          + "2. Your user id will be deleted.\n"
		                          + "3. Your linked website account (if any) will be deleted.")
		           .WithColor(Color.Blue)
		           .WithFooter("UpdateNotifier Bot")
		           .WithCurrentTimestamp()
		           .Build();

		var components = new ComponentBuilder()
		                .WithButton("Accept",  "accept_deletion",  ButtonStyle.Success)
		                .WithButton("Decline", "decline_deletion", ButtonStyle.Danger)
		                .Build();

		logger.ZLogDebug($"User {Context.User.GlobalName} is trying to disable the bot's functions and delete all user data.");

		await RespondAsync(embed: embed, components: components, ephemeral: true);
	}

	[ComponentInteraction("accept_deletion")]
	public async Task AcceptDeletion()
	{
		var userId = Context.User.Id;

		var success = await api.RemoveUserAsync(userId);

		if (success)
		{
			await RespondAsync(embeds:
			                   [
				                   new EmbedBuilder()
				                  .WithTitle("User deleted.")
				                  .WithDescription("RIP.")
				                  .WithColor(Color.Red)
				                  .Build(),
			                   ],
			                   ephemeral: true);
			logger.ZLogDebug($"User {Context.User.GlobalName} is disabled and all user data was deleted.");
		}
		else
		{
			await RespondAsync(embeds:
			                   [
				                   new EmbedBuilder()
				                  .WithTitle("Error")
				                  .WithDescription("There was an error deleting your account. Please try again later.")
				                  .WithColor(Color.Red)
				                  .Build(),
			                   ],
			                   ephemeral: true);
			logger.ZLogError($"User {Context.User.GlobalName} could not be removed from the database.");
		}
	}

	[ComponentInteraction("decline_deletion")]
	public async Task DeclineDeletion()
	{
		await RespondAsync(embeds:
		                   [
			                   new EmbedBuilder()
			                  .WithTitle("Deletion declined")
			                  .WithDescription("Nice. Continue enjoying the bot.")
			                  .WithColor(Color.Green)
			                  .Build(),
		                   ],
		                   ephemeral: true);
		logger.ZLogDebug($"User {Context.User.GlobalName} declined deletion.");
	}

	[SlashCommand("get_hash", "Get the hash of the user.")]
	public async Task GetHash()
	{
		var user = await api.GetUserAsync(Context.User.Id);
		if (!user.Exists)
		{
			await RespondAsync("This user doesn't exist.", ephemeral: true);
			return;
		}

		var hash = user.Hash;
		if (hash == null)
		{
			await RespondAsync("Something went wrong.", ephemeral: true);
			return;
		}

		await RespondAsync($"Hash: {hash}", ephemeral: true);
	}
}
