using Microsoft.Extensions.Logging;

namespace UpdateNotifier.Utilities;

public sealed class Config
{
	private readonly ILogger<Config> _logger;

	public Config(ILogger<Config> logger)
	{
		_logger = logger;
		RssFeedUrl = Environment.GetEnvironmentVariable("RSS_FEED_URL") ?? @"https://f95zone.to/sam/latest_alpha/latest_data.php?cmd=rss&cat=games";
		BotToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN")
		        ?? throw new InvalidOperationException("Bot token not found. Set the DISCORD_BOT_TOKEN environment variable.");
		XfUser = Environment.GetEnvironmentVariable("XF_USER")       ?? string.Empty;
		XfSession = Environment.GetEnvironmentVariable("XF_SESSION") ?? string.Empty;
		IsProduction = Environment.GetEnvironmentVariable("ENVIRONMENT")?.ToLower() == "production";
		SelfHosted = Environment.GetEnvironmentVariable("SELF_HOSTED")?.ToLower()   == "true";

		var guildIdStr = Environment.GetEnvironmentVariable("DISCORD_GUILD_ID");
		if (!string.IsNullOrEmpty(guildIdStr) && ulong.TryParse(guildIdStr, out var guildId))
		{
			GuildId = guildId;
		}
		else
		{
			if (!IsProduction)
				throw new InvalidOperationException("No guild ID is provided for dev mode. Set the DISCORD_GUILD_ID environment variable.");
		}

		DatabasePath = Environment.GetEnvironmentVariable("DATABASE_PATH") ?? "/data/app.db";

		var intervalStr = Environment.GetEnvironmentVariable("RSS_UPDATE_INTERVAL");
		if (!string.IsNullOrEmpty(intervalStr) && int.TryParse(intervalStr, out var minutes) && minutes > 0)
			UpdateCheckInterval = TimeSpan.FromMinutes(minutes);
		else
			UpdateCheckInterval = TimeSpan.FromMinutes(5);
	}

	public string   BotToken            { get; }
	public ulong?   GuildId             { get; }
	public bool     IsProduction        { get; }
	public bool     SelfHosted          { get; }
	public string   DatabasePath        { get; }
	public string   RssFeedUrl          { get; }
	public TimeSpan UpdateCheckInterval { get; }
	public string   XfUser              { get; }
	public string   XfSession           { get; }

	public override string ToString()
		=> $"{nameof(BotToken)}: {BotToken}, {nameof(GuildId)}: {GuildId}, {nameof(IsProduction)}: {IsProduction}, {nameof(SelfHosted)}: {SelfHosted}, {nameof(DatabasePath)}: {DatabasePath}, {nameof(UpdateCheckInterval)}: {UpdateCheckInterval}";
}