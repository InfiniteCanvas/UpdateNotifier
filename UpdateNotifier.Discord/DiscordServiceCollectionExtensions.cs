using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using UpdateNotifier.Abstractions;
using UpdateNotifier.Commands;
using UpdateNotifier.Services;

namespace UpdateNotifier.Bot;

public static class DiscordServiceCollectionExtensions
{
	/// <summary>
	///     Wires the Discord bot: gateway/interaction clients, slash commands, DM delivery and privilege checks.
	///     Must only be called when a bot token is available; the host registers no-ops otherwise.
	/// </summary>
	public static IServiceCollection AddDiscordBot(this IServiceCollection services)
	{
		var discordConfig = new DiscordSocketConfig
		{
			GatewayIntents = GatewayIntents.DirectMessages | GatewayIntents.Guilds,
			LogLevel = LogSeverity.Info,
			MessageCacheSize = 1000,
			DefaultRetryMode = RetryMode.AlwaysRetry,
			MaxWaitBetweenGuildAvailablesBeforeReady = 3000,
		};
		var interactionServiceConfig = new InteractionServiceConfig
		{
			LogLevel = LogSeverity.Info,
			DefaultRunMode = RunMode.Async,
			UseCompiledLambda = true,
			ExitOnMissingModalField = true,
			AutoServiceScopes = true,
		};
		var discordRestConfig = new DiscordRestConfig { LogLevel = LogSeverity.Info, DefaultRetryMode = RetryMode.AlwaysRetry };

		return services.AddSingleton(discordConfig)
		               .AddSingleton<DiscordSocketClient>()
		               .AddSingleton(discordRestConfig)
		               .AddSingleton<DiscordRestClient>()
		               .AddSingleton(interactionServiceConfig)
		               .AddSingleton(provider => new InteractionService(provider.GetRequiredService<DiscordSocketClient>(),
		                                                                provider.GetRequiredService<InteractionServiceConfig>()))
		               .AddSingleton<BotConfig>()
		               .AddSingleton<IDmSender, DiscordDmSender>()
		               .AddSingleton<DiscordPrivilegeChecker>()
		               .AddSingleton<IPrivilegeChecker>(provider => provider.GetRequiredService<DiscordPrivilegeChecker>())
		               .AddHostedService(provider => provider.GetRequiredService<DiscordPrivilegeChecker>())
		               .AddSingleton<CommandHandler>()
		               .AddHostedService<DiscordBotService>();
	}
}
