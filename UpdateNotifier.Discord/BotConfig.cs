using Microsoft.Extensions.Logging;
using ZLogger;

namespace UpdateNotifier.Bot;

public sealed class BotConfig
{
	public BotConfig(ILogger<BotConfig> logger)
	{
		BotToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? string.Empty;
		IsProduction = Environment.GetEnvironmentVariable("ENVIRONMENT")?.ToLower() == "production";

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

		var privilegesStr = Environment.GetEnvironmentVariable("PRIVILEGED_ROLE_IDS");
		if (!string.IsNullOrEmpty(privilegesStr))
			PrivilegedRoleIds = privilegesStr.Split(',').Where(s => ulong.TryParse(s, out _)).Select(ulong.Parse).ToArray();
		else
			PrivilegedRoleIds = [1345449839801925692, 1021813826938748979];

		var apiBaseUrl = Environment.GetEnvironmentVariable("API_BASE_URL");
		ApiBaseUrl = (string.IsNullOrEmpty(apiBaseUrl) ? "http://api:8080" : apiBaseUrl).TrimEnd('/');

		InternalApiKey = Environment.GetEnvironmentVariable("INTERNAL_API_KEY") ?? string.Empty;
		if (string.IsNullOrEmpty(InternalApiKey))
			logger.ZLogCritical($"You need to set the INTERNAL_API_KEY environment variable.");

		var pollIntervalStr = Environment.GetEnvironmentVariable("NOTIFICATION_POLL_INTERVAL");
		if (!string.IsNullOrEmpty(pollIntervalStr) && int.TryParse(pollIntervalStr, out var pollSeconds) && pollSeconds > 0)
			NotificationPollInterval = TimeSpan.FromSeconds(pollSeconds);
		else
			NotificationPollInterval = TimeSpan.FromSeconds(5);

		var syncIntervalStr = Environment.GetEnvironmentVariable("PRIVILEGE_SYNC_INTERVAL");
		if (!string.IsNullOrEmpty(syncIntervalStr) && int.TryParse(syncIntervalStr, out var syncMinutes) && syncMinutes > 0)
			PrivilegeSyncInterval = TimeSpan.FromMinutes(syncMinutes);
		else
			PrivilegeSyncInterval = TimeSpan.FromMinutes(15);
	}

	public string  BotToken          { get; }
	public ulong   GuildId           { get; }
	public bool    IsProduction      { get; }
	public ulong[] PrivilegedRoleIds { get; }

	public string    ApiBaseUrl               { get; }
	public string    InternalApiKey           { get; }
	public TimeSpan  NotificationPollInterval { get; }
	public TimeSpan  PrivilegeSyncInterval    { get; }
}
