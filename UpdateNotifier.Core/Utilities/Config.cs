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
		DatabasePath = Environment.GetEnvironmentVariable("DATABASE_PATH") ?? "/data/app.db";
		LogsFolderPath = Environment.GetEnvironmentVariable("LOGS_FOLDER") ?? "/data/logs";

		var intervalStr = Environment.GetEnvironmentVariable("RSS_UPDATE_INTERVAL");
		if (!string.IsNullOrEmpty(intervalStr) && int.TryParse(intervalStr, out var minutes) && minutes > 0)
			UpdateCheckInterval = TimeSpan.FromMinutes(minutes);
		else
			UpdateCheckInterval = TimeSpan.FromMinutes(5);

		logger.ZLogInformation($"Config: {this}");
	}

	public bool     SelfHosted          { get; }
	public string   DatabasePath        { get; }
	public string   LogsFolderPath      { get; }
	public string[] RssFeedUrls         { get; }
	public TimeSpan UpdateCheckInterval { get; }
	public string   XfUser              { get; }
	public string   XfSession           { get; }

	public const int FREE_USER_LIMIT = 69;

	public override string ToString()
		=> $"{nameof(DatabasePath)}: {DatabasePath}, {nameof(LogsFolderPath)}: {LogsFolderPath}, {nameof(UpdateCheckInterval)}: {UpdateCheckInterval}, {nameof(RssFeedUrls)}: {RssFeedUrls}";
}
