using System.Collections.Immutable;
using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using R3;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Services;

public sealed class RssMonitorService(
	ILogger<RssMonitorService> logger,
	Config                     config,
	DataContext                db,
	IHttpClientFactory         httpClientFactory)
	: BackgroundService
{
	private readonly Queue<SyndicationFeed> _feeds          = new();
	private readonly TimedSemaphore         _timedSemaphore = new(4, 4, TimeSpan.FromMinutes(1));
	private          DisposableBag          _disposable;

	public event Action<List<Game>>? GamesUpdatedEvent;

	public override void Dispose()
	{
		_disposable.Dispose();
		base.Dispose();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logger.ZLogInformation($"RssMonitorService is starting.");
		var bag = new DisposableBag(2);
		bag.Add(Observable.Interval(config.UpdateCheckInterval, stoppingToken)
		                  .TakeUntil(stoppingToken)
		                  .SubscribeAwait(CheckFeedsAndQueueNotification));
		_disposable = bag;
		await Task.Delay(-1, stoppingToken);
	}

	private async ValueTask CheckFeedsAndQueueNotification(Unit _, CancellationToken ct)
	{
		logger.ZLogTrace($"RssMonitorService is checking the feeds for updates.");
		await GetFeeds(ct);

		while (_feeds.TryDequeue(out var rawFeed))
		{
			try
			{
				await CheckFeed(rawFeed, ct);
			}
			catch (Exception e)
			{
				logger.ZLogError(e, $"RssMonitorService failed checking this feed: {rawFeed}");
			}
		}
	}

	private async Task CheckFeed(SyndicationFeed rawFeed, CancellationToken ct)
	{
		var feed = Transform(rawFeed).ToImmutableList();
		// to list so we actually fetch the query
		var toCheck = db.Games.Include(g => g.Watchers).Where(dbGame => feed.Contains(dbGame)).ToImmutableList();
		logger.ZLogTrace($"To Check: {toCheck}");
		var toAdd = feed.Except(toCheck).ToImmutableList();
		logger.ZLogTrace($"To Add: {toAdd}");
		var toUpdate = new List<Game>();

		foreach (var dbGame in toCheck)
		{
			var feedGame = feed.First(g => g.GameId == dbGame.GameId);
			if (feedGame <= dbGame) continue;
			dbGame.LastUpdated = feedGame.LastUpdated;
			dbGame.Title = feedGame.Title;
			toUpdate.Add(dbGame);
		}

		if (!toAdd.IsEmpty)
		{
			logger.ZLogInformation($"Adding [{string.Join(", ", toAdd)}] to database.");
			db.Games.AddRange(toAdd);
		}

		if (toUpdate.Count != 0)
		{
			logger.ZLogInformation($"Updating games: {string.Join(", ", toUpdate)}");
			db.UpdateRange(toUpdate);

			GamesUpdatedEvent?.Invoke(toUpdate);
		}

		logger.ZLogTrace($"To Update: {toUpdate}");

		await db.SaveChangesAsync(ct);
	}

	private async Task GetFeeds(CancellationToken ct)
	{
		var client = httpClientFactory.CreateClient("RssFeed");
		foreach (var feedUrl in config.RssFeedUrls)
		{
			await _timedSemaphore.WaitAsync(ct);
			try
			{
				await using var stream = await client.GetStreamAsync(feedUrl, ct);
				using var reader = XmlReader.Create(stream);
				_feeds.Enqueue(SyndicationFeed.Load(reader));
				logger.ZLogDebug($"RSS feed queued for updates: {feedUrl}");
			}
			catch (Exception e)
			{
				logger.ZLogError(e, $"Failed to load RSS feed. [{feedUrl}]");
			}
		}
	}


	private static IEnumerable<Game> Transform(SyndicationFeed syndicationFeed)
	{
		foreach (var item in syndicationFeed.Items)
		{
			if (!item.Id.GetSanitizedUrl(out var sanitizedUrl)) continue;
			if (!sanitizedUrl.GetThreadId(out var threadId)) continue;
			yield return new Game(title: item.Title.Text.HtmlDecode(), url: sanitizedUrl, lastUpdated: item.PublishDate.DateTime, gameId: threadId);
		}
	}
}