using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using UpdateNotifier.Abstractions;
using UpdateNotifier.Data;
using UpdateNotifier.Bot;
using UpdateNotifier.Extensions;
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

            // The AccountLinking migration rotates every extension hash - that is irreversible.
            // Snapshot the database file before touching it so an operator can roll back by restoring the copy.
            var config = scope.ServiceProvider.GetRequiredService<Config>();
            if (db.Database.GetPendingMigrations().Any() && File.Exists(config.DatabasePath))
            {
                var backupPath = config.DatabasePath + $".pre-migration-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(config.DatabasePath, backupPath); // no overwrite: a collision should fail loudly
                scope.ServiceProvider.GetRequiredService<ILogger<Program>>()
                    .ZLogWarning($"Pending database migration: backed up {config.DatabasePath} to {backupPath}.");
            }

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
            app.UseDefaultFiles(); // SPA bundle lands in wwwroot at publish time (placeholder in dev)
            app.UseStaticFiles();
            app.UseRouting();
            app.UseCors(); // parameterless: honors per-endpoint RequireCors metadata (extension endpoints only)
            app.UseRateLimiter(); // after UseRouting (reads endpoint metadata), before UseEndpoints
            app.UseEndpoints(ConfigureEndPoints);
        });

    private static void ConfigureEndPoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi();
        endpoints.MapScalarApiReference(options => options.Title = "UpdateNotifier API");
        endpoints.MapUpdateNotifierEndpoints();
        endpoints.MapFallbackToFile("index.html"); // SPA client-side routing; last so API routes win
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
                {
                    var path = description.RelativePath;
                    return path != null
                           && (path.StartsWith("api/v1/game")
                               || path.StartsWith("api/v1/auth/")
                               || path.StartsWith("api/v1/me/"));
                };
            });
        serviceCollection.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.ReferenceHandler =
                ReferenceHandler.IgnoreCycles;
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
