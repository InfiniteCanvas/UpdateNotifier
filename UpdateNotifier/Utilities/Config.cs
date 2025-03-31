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
		BotToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? string.Empty;
		XfUser = Environment.GetEnvironmentVariable("XF_USER")             ?? string.Empty;
		XfSession = Environment.GetEnvironmentVariable("XF_SESSION")       ?? string.Empty;
		IsProduction = Environment.GetEnvironmentVariable("ENVIRONMENT")?.ToLower() == "production";
		SelfHosted = Environment.GetEnvironmentVariable("SELF_HOSTED")?.ToLower()   == "true";
		DatabasePath = Environment.GetEnvironmentVariable("DATABASE_PATH") ?? "/data/app.db";
		LogsFolderPath = Environment.GetEnvironmentVariable("LOGS_FOLDER") ?? "/data/logs";

		if (string.IsNullOrEmpty(BotToken))
			logger.ZLogCritical($"You need to set the DISCORD_BOT_TOKEN environment variable.");
		var guildIdStr = Environment.GetEnvironmentVariable("DISCORD_GUILD_ID");
		if (!string.IsNullOrEmpty(guildIdStr) && ulong.TryParse(guildIdStr, out var guildId))
		{
			GuildId = guildId;
		}
		else
		{
			logger.ZLogWarning($"DISCORD_GUILD_ID environment variable is set to 1020305112368955402 as default.");
			GuildId = 1020305112368955402;
			if (!IsProduction)
				logger.ZLogCritical($"No guild ID is provided for dev mode. Set the DISCORD_GUILD_ID environment variable.");
		}

		var intervalStr = Environment.GetEnvironmentVariable("RSS_UPDATE_INTERVAL");
		if (!string.IsNullOrEmpty(intervalStr) && int.TryParse(intervalStr, out var minutes) && minutes > 0)
			UpdateCheckInterval = TimeSpan.FromMinutes(minutes);
		else
			UpdateCheckInterval = TimeSpan.FromMinutes(5);

		var privilegesStr = Environment.GetEnvironmentVariable("PRIVILEGED_ROLE_IDS");
		if (!string.IsNullOrEmpty(privilegesStr))
			PrivilegedRoleIds = privilegesStr.Split(',').Where(s => ulong.TryParse(s, out _)).Select(ulong.Parse).ToArray();
		else
			PrivilegedRoleIds = [1345449839801925692, 1021813826938748979];

		logger.ZLogInformation($"Config: {this}");
	}

	public string   BotToken            { get; }
	public ulong    GuildId             { get; }
	public bool     IsProduction        { get; }
	public bool     SelfHosted          { get; }
	public string   DatabasePath        { get; }
	public string   LogsFolderPath      { get; }
	public string[] RssFeedUrls         { get; }
	public TimeSpan UpdateCheckInterval { get; }
	public string   XfUser              { get; }
	public string   XfSession           { get; }
	public ulong[]  PrivilegedRoleIds   { get; }

	public override string ToString()
		=> $"{nameof(DatabasePath)}: {DatabasePath}, {nameof(LogsFolderPath)}: {LogsFolderPath}, {nameof(UpdateCheckInterval)}: {UpdateCheckInterval}, {nameof(RssFeedUrls)}: {RssFeedUrls}";
}