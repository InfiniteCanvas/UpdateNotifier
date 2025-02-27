using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace UpdateNotifier.Commands;

public sealed class CommandHandler(
	ILogger<CommandHandler> logger,
	IServiceProvider        services,
	DiscordSocketClient     client,
	InteractionService      interactionService)
{
	public async Task InitializeAsync()
	{
		// https://docs.discordnet.dev/guides/int_framework/intro.html#resolving-module-dependencies
		await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), services);
		client.InteractionCreated += HandleInteractionAsync;
		interactionService.SlashCommandExecuted += SlashCommandExecutedAsync;
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
			logger.ZLogError($"Slash command '{info.Name}' failed by {context.User.Username}#{context.User.Discriminator} ({context.User.Id}); Reason: {result.ErrorReason}");

		return Task.CompletedTask;
	}
}