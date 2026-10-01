using System.Net;
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
		return services.AddSingleton<Config>()
		               .AddDbContext<DataContext>(ServiceLifetime.Transient, ServiceLifetime.Transient)
		               .AddSingleton<GameInfoProvider>()
		               .AddSingleton<NotificationService>()
		               .AddHostedService(provider => provider.GetRequiredService<NotificationService>())
		               .AddSingleton<RssMonitorService>()
		               .AddHostedService(provider => provider.GetRequiredService<RssMonitorService>())
		               .AddTransient<IEndpointHandlerService, EndpointHandlerService>()
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
}
