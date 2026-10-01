using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using UpdateNotifier.Abstractions;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Requests;
using UpdateNotifier.Bot;
using UpdateNotifier.Extensions;
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
        var headless = IsHeadless();

        var host = Host.CreateDefaultBuilder(args)
            .UseDefaultServiceProvider(options => options.ValidateOnBuild = true)
            .ConfigureServices(services => ConfigureServices(services, headless))
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

            if (headless)
                scope.ServiceProvider.GetRequiredService<ILogger<Program>>()
                    .ZLogWarning(
                        $"DISABLE_DISCORD={Environment.GetEnvironmentVariable("DISABLE_DISCORD") ?? "unset"}, DISCORD_BOT_TOKEN missing or empty - running headless: the Discord bot will not start and DMs are no-ops.");
        }

        await host.RunAsync();
    }

    private static bool IsHeadless()
        => Environment.GetEnvironmentVariable("DISABLE_DISCORD")?.ToLower() == "true"
           || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN"));

    private static void ConfigureWebHost(IWebHostBuilder builder)
        => builder.Configure(app =>
        {
            app.UseCors("AllowExtension");
            app.UseRouting();
            app.UseEndpoints(ConfigureEndPoints);
        });

    private static void ConfigureEndPoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi();
        endpoints.MapScalarApiReference(options => options.Title = "UpdateNotifier API");
        endpoints.MapPost("/api/v1/games",
                async ([FromBody] GameAddRequest addRequest, IEndpointHandlerService handlerService,
                        CancellationToken ct)
                    => await handlerService.AddGameAsync(addRequest, ct))
            .WithName("AddGame")
            .WithTags("Game")
            .WithSummary("Add game for tracking")
            .WithDescription("Create a new watchlist entry for game tracking");
        endpoints.MapDelete("/api/v1/games",
                async ([FromBody] GameAddRequest addRequest, IEndpointHandlerService handlerService,
                        CancellationToken ct)
                    => await handlerService.RemoveGameAsync(addRequest, ct))
            .WithName("RemoveGame")
            .WithTags("Game")
            .WithSummary("Remove game from tracking")
            .WithDescription("Remove a watchlist entry from game tracking");
        endpoints.MapGet("/api/v1/games",
                async ([FromQuery] string userHash, IEndpointHandlerService handlerService, CancellationToken ct)
                    => await handlerService.GetWatchedGamesAsync(userHash, ct))
            .WithName("GetIsWatched")
            .WithTags("Game")
            .WithSummary("Get game tracking status")
            .WithDescription("Get game tracking status");
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

    private static void ConfigureServices(IServiceCollection serviceCollection, bool headless)
    {
        serviceCollection.AddOpenApi("v1",
            options =>
            {
                options.ShouldInclude = description =>
                    description.RelativePath != null && description.RelativePath.StartsWith("api/v1/game");
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

        serviceCollection.AddUpdateNotifierCore();

        if (headless)
            serviceCollection.AddSingleton<IDmSender, NoopDmSender>()
                .AddSingleton<IPrivilegeChecker>(provider =>
                    new InlinePrivilegeChecker(provider.GetRequiredService<Config>().SelfHosted));
        else
            serviceCollection.AddDiscordBot();
    }
}