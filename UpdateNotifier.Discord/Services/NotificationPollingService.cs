using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Bot;
using ZLogger;

namespace UpdateNotifier.Services;

/// <summary>
///     Polls the api container's durable notification queue and delivers each row as a Discord DM,
///     acking what was sent. A failed DM keeps its row un-acked so it retries (the api dead-letters
///     it after its attempt cap); a failed poll backs off exponentially so a transient api outage
///     can never kill the bot.
/// </summary>
public class NotificationPollingService(
	UpdateNotifierApiClient             apiClient,
	DiscordDmSender                     dmSender,
	BotConfig                           config,
	ILogger<NotificationPollingService> logger)
	: BackgroundService
{
	private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var delay = config.NotificationPollInterval;
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				var pending = await apiClient.GetPendingAsync(50, stoppingToken);

				var sentIds = new List<long>(pending.Count);
				foreach (var notification in pending)
				{
					try
					{
						// a swallowed user-not-found still acks: treat as delivered, like the old pipeline
						await dmSender.SendDmAsync(notification.DiscordUserId, notification.Message, stoppingToken);
						sentIds.Add(notification.Id);
					}
					catch (Exception e)
					{
						logger.ZLogError(e, $"Failed to deliver notification {notification.Id} to {notification.DiscordUserId}");
					}
				}

				if (sentIds.Count > 0)
					await apiClient.AckAsync(sentIds, stoppingToken);

				delay = config.NotificationPollInterval; // reset the backoff after a successful cycle
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception e)
			{
				delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxBackoff.Ticks));
				logger.ZLogWarning(e, $"Notification poll failed, backing off to {delay.TotalSeconds:0.#}s");
			}

			try
			{
				await Task.Delay(delay, stoppingToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}
}
