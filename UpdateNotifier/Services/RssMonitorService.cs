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
	private readonly TimedSemaphore   _timedSemaphore = new(2, 2, TimeSpan.FromMinutes(1));
	private          IDisposable      _disposable     = null!;
	private          SyndicationFeed? _feed;

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
		                  .SubscribeAwait(CheckFeedAndQueueNotification));
		_disposable = bag;
		await Task.Delay(-1, stoppingToken);
	}

	private async ValueTask CheckFeedAndQueueNotification(Unit _, CancellationToken ct)
	{
		await _timedSemaphore.WaitAsync(ct);
		logger.ZLogInformation($"RssMonitorService is checking the feed for updates.");
		GetFeed().GetAwaiter().GetResult();
		var feed = _feed == null ? [] : Transform(_feed).ToImmutableList();
		// to list so we actually fetch the query
		var toCheck = db.Games.Include(g => g.Watchers).Where(dbGame => feed.Contains(dbGame)).ToImmutableList();
		logger.ZLogTrace($"To Check: {toCheck}");
		var toAdd = feed.Except(toCheck).ToImmutableList();
		var toUpdate = new List<Game>();

		foreach (var dbGame in toCheck)
		{
			var feedGame = feed.First(g => g.GameId == dbGame.GameId);
			if (feedGame <= dbGame) continue;
			dbGame.LastUpdated = feedGame.LastUpdated;
			dbGame.Title = feedGame.Title;
			toUpdate.Add(dbGame);
		}

		if (toAdd.Any())
		{
			logger.ZLogInformation($"Adding [{string.Join(", ", toAdd)}] to database.");
			db.Games.AddRange(toAdd);
		}

		if (toUpdate.Any())
		{
			logger.ZLogInformation($"Updating games: {string.Join(", ", toUpdate)}");
			db.UpdateRange(toUpdate);

			GamesUpdatedEvent?.Invoke(toUpdate);
		}

		await db.SaveChangesAsync(ct);
	}

	private async Task GetFeed()
	{
		try
		{
			var client = httpClientFactory.CreateClient("RssFeed");
			await using var stream = await client.GetStreamAsync(config.RssFeedUrl);
			using var reader = XmlReader.Create(stream);
			_feed = SyndicationFeed.Load(reader);
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"Failed to load RSS feed.");
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