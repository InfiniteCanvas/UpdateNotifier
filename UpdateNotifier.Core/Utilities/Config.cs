using System.Net;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace UpdateNotifier.Utilities;

public sealed class Config
{
	public const string RSS_FEED_BASE = "https://f95zone.to/";

	public Config(ILogger<Config> logger)
	{
		var urls = Environment.GetEnvironmentVariable("RSS_FEED_URLS");
		if (!string.IsNullOrEmpty(urls)) RssFeedUrls = urls.Split(',');
		else
			RssFeedUrls =
			[
				@"https://f95zone.to/sam/latest_alpha/latest_data.php?cmd=rss&cat=games",
				@"https://f95zone.to/sam/latest_alpha/latest_data.php?cmd=rss&cat=animations",
				@"https://f95zone.to/sam/latest_alpha/latest_data.php?cmd=rss&cat=comics",
				@"https://f95zone.to/sam/latest_alpha/latest_data.php?cmd=rss&cat=assets",
			];
		XfUser = Environment.GetEnvironmentVariable("XF_USER")       ?? string.Empty;
		XfSession = Environment.GetEnvironmentVariable("XF_SESSION") ?? string.Empty;
		SelfHosted = Environment.GetEnvironmentVariable("SELF_HOSTED")?.ToLower() == "true";
		// default secure: only an explicit "false" (or "true") parses - anything else stays true
		CookieSecure = !bool.TryParse(Environment.GetEnvironmentVariable("COOKIE_SECURE"), out var cookieSecure) || cookieSecure;
		DatabasePath = Environment.GetEnvironmentVariable("DATABASE_PATH") ?? "/data/app.db";
		LogsFolderPath = Environment.GetEnvironmentVariable("LOGS_FOLDER") ?? "/data/logs";

		var intervalStr = Environment.GetEnvironmentVariable("RSS_UPDATE_INTERVAL");
		if (!string.IsNullOrEmpty(intervalStr) && int.TryParse(intervalStr, out var minutes) && minutes > 0)
			UpdateCheckInterval = TimeSpan.FromMinutes(minutes);
		else
			UpdateCheckInterval = TimeSpan.FromMinutes(5);

		var scrapeRateStr = Environment.GetEnvironmentVariable("THUMBNAIL_SCRAPE_RATE");
		if (!string.IsNullOrEmpty(scrapeRateStr) && int.TryParse(scrapeRateStr, out var scrapeRate) && scrapeRate > 0)
			ThumbnailScrapeRatePerMinute = scrapeRate;
		else
			ThumbnailScrapeRatePerMinute = 30;

		var sweepStr = Environment.GetEnvironmentVariable("THUMBNAIL_SWEEP_INTERVAL_MINUTES");
		if (!string.IsNullOrEmpty(sweepStr) && int.TryParse(sweepStr, out var sweepMinutes) && sweepMinutes > 0)
			ThumbnailSweepInterval = TimeSpan.FromMinutes(sweepMinutes);
		else
			ThumbnailSweepInterval = TimeSpan.FromMinutes(360);

		InternalApiKey = Environment.GetEnvironmentVariable("INTERNAL_API_KEY") ?? string.Empty;
		if (string.IsNullOrEmpty(InternalApiKey))
			logger.ZLogWarning($"INTERNAL_API_KEY is not set - the internal API endpoints will reject every caller");

		// the internal API's CIDR allowlist: an explicit override or the private/container defaults
		var cidrs = Environment.GetEnvironmentVariable("INTERNAL_API_ALLOWED_CIDRS");
		if (!string.IsNullOrEmpty(cidrs))
		{
			var networks = new List<IPNetwork>();
			foreach (var entry in cidrs.Split(','))
			{
				if (IPNetwork.TryParse(entry.Trim(), out var network))
					networks.Add(network);
				else
					logger.ZLogWarning($"Ignoring malformed CIDR '{entry.Trim()}' from INTERNAL_API_ALLOWED_CIDRS");
			}

			InternalApiAllowedCidrs = networks;
		}
		else
		{
			InternalApiAllowedCidrs =
			[
				IPNetwork.Parse("127.0.0.0/8"),    // IPv4 loopback
				IPNetwork.Parse("::1/128"),        // IPv6 loopback
				IPNetwork.Parse("10.0.0.0/8"),     // RFC1918 private
				IPNetwork.Parse("172.16.0.0/12"),  // RFC1918 private
				IPNetwork.Parse("192.168.0.0/16"), // RFC1918 private
				IPNetwork.Parse("100.64.0.0/10"),  // CGNAT (Tailscale)
				IPNetwork.Parse("fd00::/8")        // IPv6 ULA
			];
		}

		logger.ZLogInformation($"Internal API trusts CIDRs: {string.Join(", ", InternalApiAllowedCidrs)}");
		logger.ZLogInformation($"Config: {this}");
	}

	public bool     SelfHosted          { get; }
	public bool     CookieSecure        { get; }
	public string   DatabasePath        { get; }
	public string   LogsFolderPath      { get; }
	public string[] RssFeedUrls         { get; }
	public TimeSpan UpdateCheckInterval { get; }
	public string   XfUser              { get; }
	public string   XfSession           { get; }

	public int      ThumbnailScrapeRatePerMinute { get; }
	public TimeSpan ThumbnailSweepInterval       { get; }

	public string                   InternalApiKey          { get; }
	public IReadOnlyList<IPNetwork> InternalApiAllowedCidrs { get; }

	private const int DefaultFreeUserLimit = 6969;

	// static on purpose: enforcement sites (DataContext, web endpoints, tests) read it
	// without holding a Config instance; FREE_USER_LIMIT env var overrides (must be > 0)
	public static int FREE_USER_LIMIT { get; } =
		int.TryParse(Environment.GetEnvironmentVariable("FREE_USER_LIMIT"), out var limit) && limit > 0
			? limit
			: DefaultFreeUserLimit;

	public override string ToString()
		=> $"{nameof(DatabasePath)}: {DatabasePath}, {nameof(LogsFolderPath)}: {LogsFolderPath}, {nameof(UpdateCheckInterval)}: {UpdateCheckInterval}, {nameof(RssFeedUrls)}: {RssFeedUrls}, "
		   + $"{nameof(ThumbnailScrapeRatePerMinute)}: {ThumbnailScrapeRatePerMinute}, {nameof(ThumbnailSweepInterval)}: {ThumbnailSweepInterval}";
}
