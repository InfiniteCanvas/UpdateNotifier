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
	}

	public string  BotToken          { get; }
	public ulong   GuildId           { get; }
	public bool    IsProduction      { get; }
	public ulong[] PrivilegedRoleIds { get; }
}
