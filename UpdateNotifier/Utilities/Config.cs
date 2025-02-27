namespace UpdateNotifier.Utilities;

public sealed class Config
{
	public Config()
	{
		BotToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN")
		        ?? throw new InvalidOperationException("Bot token not found. Set the DISCORD_BOT_TOKEN environment variable.");
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
	public TimeSpan UpdateCheckInterval { get; }
}