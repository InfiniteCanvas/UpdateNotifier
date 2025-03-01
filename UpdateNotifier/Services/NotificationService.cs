using System.Threading.Channels;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data.Models;
using ZLogger;
using Game = UpdateNotifier.Data.Models.Game;

namespace UpdateNotifier.Services;

public sealed class NotificationService : BackgroundService
{
	private readonly DiscordSocketClient          _client;
	private readonly ILogger<NotificationService> _logger;
	private readonly RssMonitorService            _monitorService;

	private readonly Channel<Notification> _notificationQueue;

	public NotificationService(ILogger<NotificationService> logger, RssMonitorService monitorService, DiscordSocketClient client)
	{
		_client = client;
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
		var notifications = new Dictionary<User, List<Game>>();
		foreach (var update in updates)
		{
			foreach (var watcher in update.Watchers)
				if (notifications.TryGetValue(watcher, out var games))
					games.Add(update);
				else
					notifications.Add(watcher, [update]);
		}

		foreach (var notification in notifications)
		{
			_logger.ZLogTrace($"Notifying subscribers for {notification.Key}.");
			NotifyUser(notification.Key, string.Join('\n', notification.Value.Select(g => g.Url)));
		}
	}

	private void NotifyUser(User user, string message)
	{
		try
		{
			_notificationQueue.Writer.TryWrite(new Notification(user, message));
		}
		catch
		{
			_logger.ZLogError($"User {user} does not exist, cannot notify.");
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (await _notificationQueue.Reader.WaitToReadAsync(stoppingToken))
		{
			var notification = await _notificationQueue.Reader.ReadAsync(stoppingToken);
			var user = await _client.GetUserAsync(notification.User.UserId, new RequestOptions { CancelToken = stoppingToken });
			_logger.ZLogTrace($"Sending notification to {user.Username}: {notification.Message}");
			await user.SendMessageAsync(notification.Message, options: new RequestOptions { CancelToken = stoppingToken });
		}
	}

	private class Notification(User user, string message)
	{
		public readonly string Message = message;
		public readonly User   User    = user;
	}
}