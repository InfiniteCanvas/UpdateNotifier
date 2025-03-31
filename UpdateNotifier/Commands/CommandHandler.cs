using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Commands;

public sealed class CommandHandler(
	ILogger<CommandHandler> logger,
	IServiceProvider        services,
	DiscordSocketClient     client,
	Config                  config,
	InteractionService      interactionService)
{
	public async Task InitializeAsync()
	{
		// https://docs.discordnet.dev/guides/int_framework/intro.html#resolving-module-dependencies
		await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), services);
		client.InteractionCreated += HandleInteractionAsync;
		interactionService.SlashCommandExecuted += SlashCommandExecutedAsync;

		try
		{
			// Register commands globally or to a specific guild based on environment
			if (config.IsProduction)
			{
				await interactionService.RegisterCommandsGloballyAsync();
				logger.ZLogInformation($"Registered commands globally");
			}
			else
			{
				// Register commands to a specific guild for faster testing during development
				await interactionService.RegisterCommandsToGuildAsync(config.GuildId);
				logger.ZLogInformation($"Registered commands to guild {config.GuildId}");
			}
		}
		catch (Exception e)
		{
			logger.ZLogCritical(e, $"Error while registering commands");
		}
	}

	private async Task HandleInteractionAsync(SocketInteraction interaction)
	{
		try
		{
			var context = new SocketInteractionContext(client, interaction);
			var result = await interactionService.ExecuteCommandAsync(context, services);
			if (!result.IsSuccess)
			{
				logger.ZLogError($"Failed to execute interaction: {result.ErrorReason}");

				if (!interaction.HasResponded)
					await interaction.RespondAsync($"Error: {result.ErrorReason}", ephemeral: true);
			}
		}
		catch (Exception ex)
		{
			logger.ZLogError(ex, $"Exception occurred while handling interaction");

			if (!interaction.HasResponded)
				await interaction.RespondAsync("An error occurred while processing the command.", ephemeral: true);
		}
	}

	private Task SlashCommandExecutedAsync(SlashCommandInfo info, IInteractionContext context, IResult result)
	{
		if (result.IsSuccess)
			logger.ZLogTrace($"Slash command '{info.Name}' executed by {context.User.Username}#{context.User.Discriminator} ({context.User.Id})");
		else
			logger.ZLogError($"Slash command '{info.Name}' failed by {context.User.GlobalName} ({context.User.Id}); Reason: {result.ErrorReason}");

		return Task.CompletedTask;
	}
}