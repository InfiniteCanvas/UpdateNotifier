﻿using System.Net;
using System.Text.Json.Serialization;
using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Commands;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Requests;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;
using Utf8StringInterpolation;
using ZLogger;
using ZLogger.Providers;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace UpdateNotifier;

internal class Program
{
	private static async Task Main(string[] args)
	{
		var host = Host.CreateDefaultBuilder(args)
		               .UseDefaultServiceProvider(options => options.ValidateOnBuild = true)
		               .ConfigureServices(ConfigureServices)
		               .ConfigureLogging(ConfigureLogging)
		               .ConfigureHostOptions(options =>
		                                     {
			                                     options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
			                                     options.ShutdownTimeout = TimeSpan.FromSeconds(30);
			                                     options.ServicesStartConcurrently = true;
			                                     options.ServicesStopConcurrently = true;
		                                     })
		               .ConfigureWebHostDefaults(ConfigureWebHost)
		               .Build();

		using (var scope = host.Services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<DataContext>();
			await db.Database.MigrateAsync();
		}

		await host.RunAsync();
	}

	private static void ConfigureWebHost(IWebHostBuilder builder)
		=> builder.Configure(app =>
		                     {
			                     app.UseCors("AllowExtension");
			                     app.UseRouting();
			                     app.UseEndpoints(ConfigureEndPoints);
		                     });

	private static void ConfigureEndPoints(IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/api/v1/games",
		                  async ([FromBody] GameAddRequest addRequest, IEndpointHandlerService handlerService, CancellationToken ct)
			                  => await handlerService.AddGameAsync(addRequest, ct))
		         .WithName("AddGame")
		         .WithTags("Game")
		         .WithOpenApi(operation =>
		                      {
			                      operation.Summary = "Add game for tracking";
			                      operation.Description = "Create a new watchlist entry for game tracking";
			                      return operation;
		                      });
		endpoints.MapDelete("/api/v1/games",
		                    async ([FromBody] GameAddRequest addRequest, IEndpointHandlerService handlerService, CancellationToken ct)
			                    => await handlerService.RemoveGameAsync(addRequest, ct))
		         .WithName("RemoveGame")
		         .WithTags("Game")
		         .WithOpenApi(operation =>
		                      {
			                      operation.Summary = "Remove game from tracking";
			                      operation.Description = "Remove a watchlist entry from game tracking";
			                      return operation;
		                      });
		endpoints.MapGet("/api/v1/games",
		                 async ([FromQuery] string userHash, IEndpointHandlerService handlerService, CancellationToken ct)
			                 => await handlerService.GetWatchedGamesAsync(userHash, ct))
		         .WithName("GetIsWatched")
		         .WithTags("Game")
		         .WithOpenApi(operation =>
		                      {
			                      operation.Summary = "Get game tracking status";
			                      operation.Description = "Get game tracking status";
			                      return operation;
		                      });
	}

	private static void ConfigureLogging(ILoggingBuilder builder)
		=> builder.ClearProviders()
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
		                             })
		          .AddZLoggerRollingFile(options =>
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
		serviceCollection.AddOpenApi("v1",
		                             options =>
		                             {
			                             options.ShouldInclude = description => description.RelativePath != null && description.RelativePath.StartsWith("/api/v1/game");
		                             });
		serviceCollection.Configure<JsonOptions>(options =>
		                                         {
			                                         options.SerializerOptions.ReferenceHandler =
				                                         ReferenceHandler.IgnoreCycles;
		                                         });

		serviceCollection.AddCors(options =>
		                          {
			                          options.AddPolicy("AllowExtension",
			                                            builder =>
			                                            {
				                                            builder
					                                           .WithOrigins("https://f95zone.to", "http://localhost:8080", "http://localhost:5000")
					                                           .AllowAnyMethod()
					                                           .AllowAnyHeader()
					                                           .AllowCredentials()
					                                           .SetIsOriginAllowedToAllowWildcardSubdomains();
			                                            });
		                          });

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

		serviceCollection.AddSingleton(discordConfig)
		                 .AddSingleton<DiscordSocketClient>()
		                 .AddSingleton(discordRestConfig)
		                 .AddSingleton<DiscordRestClient>()
		                 .AddSingleton(interactionServiceConfig)
		                 .AddSingleton(provider => new InteractionService(provider.GetRequiredService<DiscordSocketClient>(),
		                                                                  provider.GetRequiredService<InteractionServiceConfig>()))
		                 .AddSingleton<Config>()
		                 .AddDbContext<DataContext>(ServiceLifetime.Transient, ServiceLifetime.Transient)
		                 // .AddDbContextPool<DataContext>(DbContextOptionsAction) // refactor later; move methods out of DataContext
		                 .AddSingleton<PrivilegeCheckerService>()
		                 .AddHostedService(provider => provider.GetRequiredService<PrivilegeCheckerService>())
		                 .AddTransient<IEndpointHandlerService, EndpointHandlerService>()
		                 .AddSingleton<CommandHandler>()
		                 .AddHostedService<DiscordBotService>()
		                 .AddSingleton<NotificationService>()
		                 .AddHostedService(provider => provider.GetRequiredService<NotificationService>())
		                 .AddSingleton<RssMonitorService>()
		                 .AddHostedService(provider => provider.GetRequiredService<RssMonitorService>())
		                 .AddSingleton<GameInfoProvider>()
		                 .AddHttpClient("RssFeed",
		                                (provider, client) =>
		                                {
			                                var config = provider.GetRequiredService<Config>();
			                                client.BaseAddress = new Uri(Config.RSS_FEED_BASE);
		                                })
		                 .ConfigurePrimaryHttpMessageHandler(provider =>
		                                                     {
			                                                     var config = provider.GetRequiredService<Config>();
			                                                     var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
			                                                     handler.CookieContainer.Add(new Uri(Config.RSS_FEED_BASE), new Cookie("xf_user",    config.XfUser));
			                                                     handler.CookieContainer.Add(new Uri(Config.RSS_FEED_BASE), new Cookie("xf_session", config.XfSession));
			                                                     return handler;
		                                                     });
	}
}