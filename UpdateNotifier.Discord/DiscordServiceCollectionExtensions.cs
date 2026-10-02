using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using UpdateNotifier.Commands;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Bot;

public static class DiscordServiceCollectionExtensions
{
	/// <summary>
	///     Wires the standalone bot process: gateway/interaction clients, slash commands, DM delivery,
	///     the internal api client, the notification poller and the privilege syncer.
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

		services.AddHttpClient<UpdateNotifierApiClient>((provider, client) =>
		{
			var botConfig = provider.GetRequiredService<BotConfig>();
			client.BaseAddress = new Uri(botConfig.ApiBaseUrl);
			client.DefaultRequestHeaders.Add("X-Internal-Api-Key", botConfig.InternalApiKey);
		});

		return services.AddSingleton(discordConfig)
		               .AddSingleton<DiscordSocketClient>()
		               .AddSingleton(discordRestConfig)
		               .AddSingleton<DiscordRestClient>()
		               .AddSingleton(interactionServiceConfig)
		               .AddSingleton(provider => new InteractionService(provider.GetRequiredService<DiscordSocketClient>(),
		                                                                provider.GetRequiredService<InteractionServiceConfig>()))
		               .AddSingleton<BotConfig>()
		               .AddSingleton<Config>()
		               .AddSingleton<DiscordDmSender>()
		               .AddSingleton<DiscordPrivilegeChecker>()
		               .AddHostedService(provider => provider.GetRequiredService<DiscordPrivilegeChecker>())
		               .AddSingleton<CommandHandler>()
		               .AddHostedService<DiscordBotService>()
		               .AddHostedService<NotificationPollingService>()
		               .AddHostedService<PrivilegeSyncer>();
	}
}
