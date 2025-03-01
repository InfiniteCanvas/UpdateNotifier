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
using Utf8StringInterpolation;
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
		      .AddZLoggerConsole(options =>
		                         {
			                         options.UsePlainTextFormatter(formatter =>
			                                                       {
				                                                       formatter.SetPrefixFormatter($"{0}|{1:short}| ",
				                                                                                    (in MessageTemplate template, in LogInfo info)
					                                                                                    => template.Format(info.Timestamp, info.LogLevel));
				                                                       formatter.SetSuffixFormatter($" ({0})",
				                                                                                    (in MessageTemplate template, in LogInfo info)
					                                                                                    => template.Format(info.Category));
				                                                       formatter.SetExceptionFormatter((writer, ex)
					                                                                                       => Utf8String.Format(writer,
						                                                                                       $"{ex.Message}"));
			                                                       });
		                         })
		      .AddZLoggerRollingFile((options, provider) =>
		                             {
			                             var logFolder = Environment.GetEnvironmentVariable("LOGS_FOLDER") ?? "/data/logs";
			                             options.RollingInterval = RollingInterval.Day;
			                             options.RollingSizeKB = 10 * 1024;
			                             options.FullMode = BackgroundBufferFullMode.Grow;
			                             options.FilePathSelector = (timestamp, sequenceNumber)
				                                                        => Path.Combine(logFolder,
				                                                                        $"{timestamp.ToLocalTime():yyyy-MM-dd}_{sequenceNumber:000}.log");
			                             options.UsePlainTextFormatter(formatter =>
			                                                           {
				                                                           formatter.SetPrefixFormatter($"{0}|{1:short}| ",
				                                                                                        (in MessageTemplate template, in LogInfo info)
					                                                                                        => template.Format(info.Timestamp, info.LogLevel));
				                                                           formatter.SetSuffixFormatter($" ({0})",
				                                                                                        (in MessageTemplate template, in LogInfo info)
					                                                                                        => template.Format(info.Category));
				                                                           formatter.SetExceptionFormatter((writer, ex)
					                                                                                           => Utf8String.Format(writer,
						                                                                                           $"{ex.Message}"));
			                                                           });
		                             })
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