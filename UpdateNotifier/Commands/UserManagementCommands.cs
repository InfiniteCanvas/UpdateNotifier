using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data;
using ZLogger;

namespace UpdateNotifier.Commands;

public sealed class UserManagementCommands(ILogger<UserManagementCommands> logger, DataContext db) : InteractionModuleBase<SocketInteractionContext>
{
	[SlashCommand("enable", "Enable the bot's functions by accepting the Terms of Service")]
	public async Task EnableBot()
	{
		var userId = Context.User.Id;

		// Check if user already exists and has accepted ToS
		var userExists = db.UserExists(userId);

		if (userExists)
		{
			await RespondAsync(embed: new EmbedBuilder()
			                         .WithTitle("Already Enabled")
			                         .WithDescription("You have already accepted the Terms of Service. The bot is enabled for your account.")
			                         .WithColor(Color.Green)
			                         .Build(),
			                   ephemeral: true);
			return;
		}

		var embed = new EmbedBuilder()
		           .WithTitle("Terms of Service")
		           .WithDescription("By accepting these terms, you agree to the following:\n\n"
		                          + "1. Your Discord user ID will be stored in our database to manage your game watchlist.\n"
		                          + "2. You will receive direct message notifications when games in your watchlist have updates.\n"
		                          + "3. Those messages will contain NSFW content.\n"
		                          + "4. You can remove your data at any time using the `/disable` command.\n"
		                          + "5. We do not share your data with any third parties.\n\n"
		                          + "Do you accept these terms?")
		           .WithColor(Color.Blue)
		           .WithFooter("UpdateNotifier Bot")
		           .WithCurrentTimestamp()
		           .Build();

		var components = new ComponentBuilder()
		                .WithButton("Accept",  "accept_tos",  ButtonStyle.Success)
		                .WithButton("Decline", "decline_tos", ButtonStyle.Danger)
		                .Build();

		logger.ZLogDebug($"User {userId} is trying to enable the bot's functions by accepting the Terms of Service.");

		await RespondAsync(embed: embed, components: components, ephemeral: true);
	}

	[ComponentInteraction("accept_tos")]
	public async Task AcceptTos()
	{
		var userId = Context.User.Id;

		// Add user to database
		var success = db.AddUser(userId);

		if (success)
		{
			await RespondAsync(embeds:
			                   [
				                   new EmbedBuilder()
					                  .WithTitle("Terms Accepted")
					                  .WithDescription("Nice. You can now use all bot functions and add games to your watchlist.")
					                  .WithColor(Color.Green)
					                  .Build(),
			                   ],
			                   ephemeral: true);
			logger.ZLogInformation($"User {userId} added to the database.");
		}
		else
		{
			await RespondAsync(embeds:
			                   [
				                   new EmbedBuilder()
					                  .WithTitle("Error")
					                  .WithDescription("There was an error enabling your account. Please try again later.")
					                  .WithColor(Color.Red)
					                  .Build(),
			                   ],
			                   ephemeral: true);
			logger.ZLogError($"Error adding user {userId} to the database.");
		}
	}

	[ComponentInteraction("decline_tos")]
	public async Task DeclineTos()
		=> await RespondAsync(embeds:
		                      [
			                      new EmbedBuilder()
				                     .WithTitle("Terms Declined")
				                     .WithDescription("You have declined the Terms of Service. Bot functions will not be enabled for your account.")
				                     .WithColor(Color.Red)
				                     .Build(),
		                      ],
		                      ephemeral: true);

	[SlashCommand("disable", "Disable the bot's functions and delete all user data.")]
	public async Task DisableBot()
	{
		var userId = Context.User.Id;
		if (!db.UserExists(userId))
		{
			await RespondAsync("This user doesn't exist.", ephemeral: true);
			return;
		}

		var embed = new EmbedBuilder()
		           .WithTitle("Are you sure?")
		           .WithDescription("By disabling this bot's service, the following will happen:\n\n"
		                          + "1. The watchlist associated with your user id will be deleted.\n"
		                          + "2. Your user id will be deleted.")
		           .WithColor(Color.Blue)
		           .WithFooter("UpdateNotifier Bot")
		           .WithCurrentTimestamp()
		           .Build();

		var components = new ComponentBuilder()
		                .WithButton("Accept",  "accept_deletion",  ButtonStyle.Success)
		                .WithButton("Decline", "decline_deletion", ButtonStyle.Danger)
		                .Build();

		logger.ZLogDebug($"User {userId} is trying to disable the bot's functions and delete all user data.");

		await RespondAsync(embed: embed, components: components, ephemeral: true);
	}

	[ComponentInteraction("accept_deletion")]
	public async Task AcceptDeletion()
	{
		var userId = Context.User.Id;

		var success = db.RemoveUser(userId);

		if (success)
			await RespondAsync(embeds:
			                   [
				                   new EmbedBuilder()
					                  .WithTitle("User deleted.")
					                  .WithDescription("RIP.")
					                  .WithColor(Color.Red)
					                  .Build(),
			                   ],
			                   ephemeral: true);
		else
			await RespondAsync(embeds:
			                   [
				                   new EmbedBuilder()
					                  .WithTitle("Error")
					                  .WithDescription("There was an error deleting your account. Please try again later.")
					                  .WithColor(Color.Red)
					                  .Build(),
			                   ],
			                   ephemeral: true);
	}

	[ComponentInteraction("decline_deletion")]
	public async Task DeclineDeletion()
		=> await RespondAsync(embeds:
		                      [
			                      new EmbedBuilder()
				                     .WithTitle("Deletion declined")
				                     .WithDescription("Nice. Continue enjoying the bot.")
				                     .WithColor(Color.Green)
				                     .Build(),
		                      ],
		                      ephemeral: true);
}