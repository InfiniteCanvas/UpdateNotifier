using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace UpdateNotifier.Utilities;

public class PrivilegeCheckerService(Config config, ILogger<PrivilegeCheckerService> logger, DiscordRestClient restClient) : BackgroundService
{
	private RestGuild? _guild;

	public bool IsPrivileged(SocketGuildUser user)
		=> config.SelfHosted
		|| user.GuildPermissions.Administrator
		|| user.GuildPermissions.ManageRoles
		|| user.GuildPermissions.ModerateMembers
		|| user.Roles.Any(r => config.PrivilegedRoleIds.Contains(r.Id));

	// make it cache privileged userIds in db later
	public async ValueTask<bool> IsPrivileged(ulong userId)
	{
		if (config.SelfHosted)
		{
			logger.ZLogDebug($"Privileged because Self hosted");
			return true;
		}

		if (_guild == null)
		{
			logger.ZLogDebug($"Could not find guild {config.GuildId}");
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

		return user.RoleIds.Any(r => config.PrivilegedRoleIds.Contains(r));
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (restClient.LoginState != LoginState.LoggedIn)
			await restClient.LoginAsync(TokenType.Bot, config.BotToken);
		_guild = await restClient.GetGuildAsync(config.GuildId);
		logger.ZLogInformation($"Connected to server[{config.GuildId}]: {_guild.Name}");
		logger.ZLogInformation($"Privileged role ids: {config.PrivilegedRoleIds}");

		await Task.Delay(-1, stoppingToken);
	}
}