using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Abstractions;
using UpdateNotifier.Data.Models;
using ZLogger;
using Game = UpdateNotifier.Data.Models.Game;

namespace UpdateNotifier.Services;

public sealed class NotificationService : BackgroundService
{
	private readonly IDmSender                    _dmSender;
	private readonly ILogger<NotificationService> _logger;
	private readonly RssMonitorService            _monitorService;

	private readonly Channel<Notification> _notificationQueue;

	public NotificationService(ILogger<NotificationService> logger, RssMonitorService monitorService, IDmSender dmSender)
	{
		_dmSender = dmSender;
		_logger = logger;
		_monitorService = monitorService;
		_notificationQueue = Channel.CreateUnbounded<Notification>();
		_monitorService.GamesUpdatedEvent += OnGamesUpdated;
		logger.ZLogInformation($"NotificationService is starting.");
	}

	private void Dispose(bool disposing)
	{
		if (disposing) _monitorService.GamesUpdatedEvent -= OnGamesUpdated;
	}

	public override void Dispose()
	{
		Dispose(true);
		base.Dispose();
	}

	private void OnGamesUpdated(List<Game> updates)
	{
		_logger.ZLogInformation($"Games updated [{updates.Count}]; Notifying subscribers..");
		// project to (userId, update) tuples; web-only accounts have no Discord identity to DM
		var notifications = new Dictionary<ulong, List<Game>>();
		foreach (var update in updates)
		{
			foreach (var watcher in update.Watchers)
			{
				var userId = watcher.User?.UserId;
				if (userId == null) continue;

				if (notifications.TryGetValue(userId.Value, out var games))
					games.Add(update);
				else
					notifications.Add(userId.Value, [update]);
			}
		}

		foreach (var notification in notifications)
		{
			_logger.ZLogTrace($"Notifying subscribers for user {notification.Key}.");
			NotifyUser(notification.Key, string.Join('\n', notification.Value.Select(g => g.Url)));
		}
	}

	private void NotifyUser(ulong userId, string message)
	{
		try
		{
			_notificationQueue.Writer.TryWrite(new Notification(userId, message));
		}
		catch
		{
			_logger.ZLogError($"User {userId} does not exist, cannot notify.");
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (await _notificationQueue.Reader.WaitToReadAsync(stoppingToken))
		{
			var notification = await _notificationQueue.Reader.ReadAsync(stoppingToken);
			try
			{
				await _dmSender.SendDmAsync(notification.UserId, notification.Message, stoppingToken);
			}
			catch (Exception e)
			{
				// one failing DM must not kill this BackgroundService - and with
				// BackgroundServiceExceptionBehavior.StopHost it would take the whole host down
				_logger.ZLogError(e, $"Failed to send notification to user {notification.UserId}.");
			}
		}
	}

	private sealed class Notification(ulong userId, string message)
	{
		public readonly string Message = message;
		public readonly ulong   UserId  = userId;
	}
}
