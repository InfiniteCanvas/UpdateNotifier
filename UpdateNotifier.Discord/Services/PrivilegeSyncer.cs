using Discord;
using Discord.Rest;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Bot;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Services;

/// <summary>
///     Pushes the guild's privileged member set to the api container (REST member listing only - no
///     extra gateway intents). The api uses it to lift the free-tier watchlist limit for those users;
///     a sync only happens when the set actually changed since the last successful push.
/// </summary>
public class PrivilegeSyncer(
	DiscordPrivilegeChecker privilegeChecker,
	DiscordRestClient        restClient,
	UpdateNotifierApiClient  apiClient,
	BotConfig                botConfig,
	Config                   config,
	ILogger<PrivilegeSyncer> logger)
	: BackgroundService
{
	private HashSet<ulong> _lastSynced = [];

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (config.SelfHosted)
		{
			logger.ZLogInformation($"Skipping privilege sync (self-hosted)");
			return;
		}

		// the checker owns the startup login; waiting for its guild also guarantees the rest client is ready
		var guild = await privilegeChecker.GuildReady.WaitAsync(stoppingToken);

		if (restClient.LoginState != LoginState.LoggedIn)
			await restClient.LoginAsync(TokenType.Bot, botConfig.BotToken);

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await SyncOnceAsync(guild, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception e)
			{
				logger.ZLogWarning(e, $"Privilege sync failed, keeping the interval");
			}

			try
			{
				await Task.Delay(botConfig.PrivilegeSyncInterval, stoppingToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private async Task SyncOnceAsync(RestGuild guild, CancellationToken ct)
	{
		var privileged = new HashSet<ulong>();
		await foreach (var page in guild.GetUsersAsync())
			foreach (var user in page)
				if (privilegeChecker.IsPrivileged(user))
					privileged.Add(user.Id);

		if (privileged.SetEquals(_lastSynced))
			return;

		await apiClient.SyncPrivilegesAsync(privileged, ct);
		_lastSynced = privileged;
		logger.ZLogInformation($"Synced {privileged.Count} privileged user(s)");
	}
}
