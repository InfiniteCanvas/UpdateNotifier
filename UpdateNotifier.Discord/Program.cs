using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Bot;
using Utf8StringInterpolation;
using ZLogger;
using ZLogger.Providers;

namespace UpdateNotifier;

internal class Program
{
	private static async Task Main(string[] args)
	{
		using var host = Host.CreateDefaultBuilder(args)
		                     .UseDefaultServiceProvider(options => options.ValidateOnBuild = true)
		                     .ConfigureServices(services => services.AddDiscordBot())
		                     .ConfigureLogging(ConfigureLogging)
		                     .ConfigureHostOptions(options =>
		                     {
			                     options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
			                     options.ShutdownTimeout = TimeSpan.FromSeconds(30);
			                     options.ServicesStartConcurrently = true;
			                     options.ServicesStopConcurrently = true;
		                     })
		                     .Build();

		if (!TryValidateStartup(host))
			return;

		await host.RunAsync();
	}

	/// <summary>
	///     The bot cannot run without its token or the shared internal api key. Failing fast with a
	///     non-zero exit code keeps the failure visible in docker instead of crash-looping silently.
	/// </summary>
	private static bool TryValidateStartup(IHost host)
	{
		var botConfig = host.Services.GetRequiredService<BotConfig>();

		var missing = new List<string>();
		if (string.IsNullOrEmpty(botConfig.BotToken)) missing.Add("DISCORD_BOT_TOKEN");
		if (string.IsNullOrEmpty(botConfig.InternalApiKey)) missing.Add("INTERNAL_API_KEY");

		if (missing.Count == 0) return true;

		host.Services.GetRequiredService<ILogger<Program>>()
		    .ZLogError($"Missing required environment variable(s): {string.Join(", ", missing)} - the bot cannot start without them.");
		Environment.ExitCode = 1;
		return false;
	}

	private static void ConfigureLogging(ILoggingBuilder builder)
	{
		var zLogger = builder.ClearProviders()
		                     .AddZLoggerConsole(options =>
		                     {
			                     options.UsePlainTextFormatter(formatter =>
			                     {
				                     formatter.SetPrefixFormatter($"{0}|{1:short}| ",
				                         (in MessageTemplate template, in LogInfo info)
					                         => template.Format(info.Timestamp, info.LogLevel));
				                     formatter.SetSuffixFormatter($" ({0}, {1})",
				                         (in MessageTemplate template, in LogInfo info)
					                         => template.Format(info.Category, info.LineNumber));
				                     formatter.SetExceptionFormatter((writer, ex)
					                         => Utf8String.Format(writer,
						                         $"{ex.Message}"));
			                     });
		                     });

		// rolling file logging is opt-in via LOGS_FOLDER; the container runs console-only
		// (docker logs collects it) so no volume is needed for the bot
		var logsFolder = Environment.GetEnvironmentVariable("LOGS_FOLDER");
		if (!string.IsNullOrEmpty(logsFolder))
			zLogger.AddZLoggerRollingFile(options =>
			{
				options.RollingInterval = RollingInterval.Day;
				options.RollingSizeKB = 10 * 1024;
				options.FullMode = BackgroundBufferFullMode.Grow;
				options.FilePathSelector = (timestamp, sequenceNumber)
					=> Path.Combine(logsFolder,
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
			});

		builder.SetMinimumLevel(LogLevel.Trace);
	}
}
