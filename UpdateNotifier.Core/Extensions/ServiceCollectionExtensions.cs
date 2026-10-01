using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UpdateNotifier.Data;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Extensions;

public static class ServiceCollectionExtensions
{
	/// <summary>
	///     Registers the platform-independent core: configuration, database, RSS monitor and the notification pipeline.
	///     Discord is wired separately (or replaced by no-ops) by the host.
	/// </summary>
	public static IServiceCollection AddUpdateNotifierCore(this IServiceCollection services)
	{
		// policy + endpoints colocate in Core; the host only turns the middleware on.
		// Credentials are NOT allowed: the extension endpoints authenticate by hash, not cookies,
		// so the browser never needs its credentialed CORS request headers here.
		services.AddCors(options => options.AddPolicy(WebEndpointExtensions.CorsPolicyName,
		                               policy => policy.WithOrigins("https://f95zone.to", "http://localhost:8080", "http://localhost:5000")
		                                               .AllowAnyMethod()
		                                               .AllowAnyHeader()));

		services.AddRateLimiter(options =>
		{
			options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			options.OnRejected = (context, cancellationToken)
				=> new ValueTask(context.HttpContext.Response.WriteAsync("Too many requests.", cancellationToken));
			options.AddPolicy(WebEndpointExtensions.AuthRateLimitPolicy,
			                  http => FixedWindowPartition(http, 10));
			options.AddPolicy(WebEndpointExtensions.MutationRateLimitPolicy,
			                  http => FixedWindowPartition(http, 30));
		});

		return services.AddSingleton<Config>()
		               .AddDbContext<DataContext>(ServiceLifetime.Transient, ServiceLifetime.Transient)
		               .AddSingleton<GameInfoProvider>()
		               .AddSingleton<NotificationService>()
		               .AddHostedService(provider => provider.GetRequiredService<NotificationService>())
		               .AddSingleton<RssMonitorService>()
		               .AddHostedService(provider => provider.GetRequiredService<RssMonitorService>())
		               .AddTransient<IEndpointHandlerService, EndpointHandlerService>()
		               .AddTransient<IWebAuthService, WebAuthService>()
		               .AddHttpClient("RssFeed",
		                              (provider, client) =>
		                              {
			                              _ = provider.GetRequiredService<Config>();
			                              client.BaseAddress = new Uri(Config.RSS_FEED_BASE);
		                              })
		               .ConfigurePrimaryHttpMessageHandler(provider =>
		                                                   {
		                                                   var config = provider.GetRequiredService<Config>();
		                                                   var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
		                                                   handler.CookieContainer.Add(new Uri(Config.RSS_FEED_BASE), new Cookie("xf_user",    config.XfUser));
		                                                   handler.CookieContainer.Add(new Uri(Config.RSS_FEED_BASE), new Cookie("xf_session", config.XfSession));
		                                                   return handler;
		                                                   })
		               .Services;
	}

	/// <summary>Fixed-window partition keyed by remote IP; queue limit 0 rejects immediately instead of queueing.</summary>
	private static RateLimitPartition<string> FixedWindowPartition(HttpContext http, int permitLimit)
		=> RateLimitPartition.GetFixedWindowLimiter(
			http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			_ => new FixedWindowRateLimiterOptions
			{
				PermitLimit = permitLimit,
				Window = TimeSpan.FromSeconds(60),
				QueueLimit = 0
			});
}
