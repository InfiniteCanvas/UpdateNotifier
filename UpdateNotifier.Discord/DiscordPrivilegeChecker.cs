using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Abstractions;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Bot;

public class DiscordPrivilegeChecker(Config config, BotConfig botConfig, ILogger<DiscordPrivilegeChecker> logger, DiscordRestClient restClient)
	: BackgroundService, IPrivilegeChecker
{
	private readonly TaskCompletionSource<RestGuild> _guildReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private RestGuild? _guild;

	/// <summary>
	///     Completes once <see cref="ExecuteAsync" /> has logged in and resolved the guild; the
	///     privilege syncer awaits this instead of racing the startup login.
	/// </summary>
	public Task<RestGuild> GuildReady => _guildReady.Task;

	// works for SocketGuildUser (commands) and RestGuildUser (privilege syncer) alike
	public bool IsPrivileged(IGuildUser user)
		=> config.SelfHosted
		|| user.GuildPermissions.Administrator
		|| user.GuildPermissions.ManageRoles
		|| user.GuildPermissions.ModerateMembers
		|| user.RoleIds.Any(r => botConfig.PrivilegedRoleIds.Contains(r));

	// make it cache privileged userIds in db later
	public async ValueTask<bool> IsPrivilegedAsync(ulong userId, CancellationToken ct = default)
	{
		if (config.SelfHosted)
		{
			logger.ZLogDebug($"Privileged because Self hosted");
			return true;
		}

		if (_guild == null)
		{
			logger.ZLogDebug($"Could not find guild {botConfig.GuildId}");
			return false;
		}

		var user = await _guild.GetUserAsync(userId);
		if (user == null)
		{
			logger.ZLogDebug($"Could not find user {userId}");
			return false;
		}

		if (user.GuildPermissions.Administrator || user.GuildPermissions.ManageRoles || user.GuildPermissions.ModerateMembers)
		{
			logger.ZLogDebug($"Privileged user due to permissions");
			return true;
		}

		return user.RoleIds.Any(r => botConfig.PrivilegedRoleIds.Contains(r));
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (restClient.LoginState != LoginState.LoggedIn)
			await restClient.LoginAsync(TokenType.Bot, botConfig.BotToken);
		_guild = await restClient.GetGuildAsync(botConfig.GuildId);
		logger.ZLogInformation($"Connected to server[{botConfig.GuildId}]: {_guild.Name}");
		logger.ZLogInformation($"Privileged role ids: {string.Join(' ', botConfig.PrivilegedRoleIds)}");
		_guildReady.TrySetResult(_guild);

		await Task.Delay(-1, stoppingToken);
	}
}
