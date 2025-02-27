using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Commands;
using UpdateNotifier.Data;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;
using ZLogger;
using ZLogger.Providers;

namespace UpdateNotifier;

internal class Program
{
	private static async Task Main(string[] args)
	{
		var host = Host.CreateDefaultBuilder(args)
		               .ConfigureServices(ConfigureServices)
		               .ConfigureLogging(ConfigureLogging)
		               .ConfigureHostOptions(options =>
		                                     {
			                                     options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
			                                     options.ShutdownTimeout = TimeSpan.FromSeconds(30);
			                                     options.ServicesStartConcurrently = true;
			                                     options.ServicesStopConcurrently = true;
		                                     })
		               .Build();
		await host.RunAsync();
	}

	private static void ConfigureLogging(ILoggingBuilder obj)
		=> obj.ClearProviders()
		      .AddZLoggerConsole()
		      .AddZLoggerRollingFile((timestamp, sequenceNumber) => $"logs/{timestamp.ToLocalTime():yyyy-MM-dd}_{sequenceNumber:000}.log",
		                             RollingInterval.Day,
		                             5 * 1024 * 1024)
		      .SetMinimumLevel(LogLevel.Trace);

	private static void ConfigureServices(IServiceCollection serviceCollection)
	{
		var discordConfig = new DiscordSocketConfig
		{
			GatewayIntents = GatewayIntents.DirectMessages | GatewayIntents.Guilds | GatewayIntents.GuildMembers,
			LogLevel = LogSeverity.Info,
			AlwaysDownloadUsers = true,
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

		serviceCollection.AddSingleton(discordConfig)
		                 .AddSingleton<DiscordSocketClient>()
		                 .AddSingleton(discordRestConfig)
		                 .AddSingleton<DiscordRestClient>()
		                 .AddSingleton(interactionServiceConfig)
		                 .AddSingleton(provider => new InteractionService(provider.GetRequiredService<DiscordSocketClient>(),
		                                                                  provider.GetRequiredService<InteractionServiceConfig>()))
		                 .AddSingleton<Config>()
		                 .AddSingleton<CommandHandler>()
		                 .AddHostedService<DiscordBotService>()
		                 .AddSingleton<NotificationService>()
		                 .AddHostedService(provider => provider.GetRequiredService<NotificationService>())
		                 .AddSingleton<RssMonitorService>()
		                 .AddHostedService(provider => provider.GetRequiredService<RssMonitorService>())
		                 .AddSingleton<DataContext>()
		                 .AddHttpClient();
	}
}