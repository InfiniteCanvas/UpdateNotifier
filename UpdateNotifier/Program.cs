using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using UpdateNotifier.Data;
using UpdateNotifier.Extensions;
using UpdateNotifier.Utilities;
using Utf8StringInterpolation;
using ZLogger;
using ZLogger.Providers;
using IPNetwork = System.Net.IPNetwork;
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
        }

        await host.RunAsync();
    }

    private static void ConfigureWebHost(IWebHostBuilder builder)
        => builder.Configure(app =>
        {
            // Trust X-Forwarded-* headers only from the proxy/container networks below (docker &
            // traefik bridges, Tailscale): per-IP rate limiting stays correct behind Traefik while
            // public clients - whose socket IP is not a known proxy - cannot spoof the header.
            var forwardedHeaders = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };
            forwardedHeaders.KnownIPNetworks.Clear();
            forwardedHeaders.KnownIPNetworks.Add(IPNetwork.Parse("127.0.0.0/8"));    // IPv4 loopback
            forwardedHeaders.KnownIPNetworks.Add(IPNetwork.Parse("::1/128"));        // IPv6 loopback
            forwardedHeaders.KnownIPNetworks.Add(IPNetwork.Parse("10.0.0.0/8"));     // RFC1918 private
            forwardedHeaders.KnownIPNetworks.Add(IPNetwork.Parse("172.16.0.0/12"));  // RFC1918 private (docker bridges)
            forwardedHeaders.KnownIPNetworks.Add(IPNetwork.Parse("192.168.0.0/16")); // RFC1918 private
            forwardedHeaders.KnownIPNetworks.Add(IPNetwork.Parse("100.64.0.0/10"));  // CGNAT (Tailscale)
            forwardedHeaders.KnownIPNetworks.Add(IPNetwork.Parse("fd00::/8"));       // IPv6 ULA
            app.UseForwardedHeaders(forwardedHeaders);

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
        endpoints.MapInternalEndpoints();
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
    }
}
