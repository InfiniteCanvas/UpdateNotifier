using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Communication;
using UpdateNotifier.Services;
using ZLogger;

namespace UpdateNotifier.Commands;

public sealed class LinkCommands(ILogger<LinkCommands> logger, UpdateNotifierApiClient api)
	: InteractionModuleBase<SocketInteractionContext>
{
	[SlashCommand("link", "Link your Discord account to your UpdateNotifier website account")]
	public async Task LinkAccount([Summary(description: "The linking code from the website's \"Link Discord\" section")] string code)
	{
		logger.ZLogDebug($"User {Context.User.Id} is trying to link their Discord account to a website account.");

		LinkConsumeResponse outcome;
		try
		{
			outcome = await api.ConsumeLinkCodeAsync(code, Context.User.Id, Context.User.GlobalName ?? Context.User.Username);
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"[{e.GetType()}]Failed to consume link code for user {Context.User.Id}");
			await RespondAsync(embed: new EmbedBuilder()
			                         .WithTitle("Error")
			                         .WithDescription("Something went wrong while linking. Please try again.")
			                         .WithColor(Color.Red)
			                         .Build(),
			                   ephemeral: true);
			return;
		}

		var (title, color, description) = outcome.Kind switch
		{
			LinkOutcomeKind.NotEnabled => ("Not Enabled", Color.Blue, $"{outcome.Message}\nPlease run `/enable` first, then try again."),
			LinkOutcomeKind.Invalid    => ("Invalid Code", Color.Red, outcome.Message),
			LinkOutcomeKind.Conflict   => ("Already Linked", Color.Red, outcome.Message),
			_                          => ("Account Linked", Color.Green, outcome.Message)
		};

		await RespondAsync(embed: new EmbedBuilder()
		                         .WithTitle(title)
		                         .WithDescription(description)
		                         .WithColor(color)
		                         .Build(),
		                   ephemeral: true);

		// same success kinds as LinkOutcome.IsSuccess: Linked, Merged or AlreadyLinked
		if (outcome.Kind is LinkOutcomeKind.Linked or LinkOutcomeKind.Merged or LinkOutcomeKind.AlreadyLinked)
			logger.ZLogInformation($"User {Context.User.GlobalName} linked their Discord account to a website account ({outcome.Kind}).");
	}
}
