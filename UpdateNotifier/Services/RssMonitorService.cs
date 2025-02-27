using System.Net;
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

public sealed class RssMonitorService : BackgroundService
{
	private readonly Config                     _config;
	private readonly DataContext                _db;
	private readonly ILogger<RssMonitorService> _logger;
	private readonly CookieContainer            _cookieContainer;
	private          IDisposable                _disposable = null!;
	private          SyndicationFeed?           _feed;
	private readonly HttpClient                 _httpClient;

	public RssMonitorService(ILogger<RssMonitorService> logger,
	                         Config                     config,
	                         DataContext                db)
	{
		_logger = logger;
		_config = config;
		_db = db;
		_cookieContainer = new CookieContainer();
		_httpClient = new HttpClient(new HttpClientHandler { CookieContainer = _cookieContainer, UseCookies = true });
	}

	public event Action<List<Game>>? GamesUpdatedEvent;

	private void Dispose(bool disposing)
	{
		if (disposing) _disposable.Dispose();
	}

	public override void Dispose()
	{
		Dispose(true);
		base.Dispose();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.ZLogInformation($"RssMonitorService is starting.");
		_disposable = Observable.Interval(TimeSpan.FromMinutes(1), stoppingToken)
		                        .TakeUntil(stoppingToken)
		                        .Subscribe(CheckFeedAndQueueNotification);
		await Task.Delay(-1, stoppingToken);
	}

	private void CheckFeedAndQueueNotification(Unit _)
	{
		_logger.ZLogInformation($"RssMonitorService is checking the feed for updates.");
		GetFeed().GetAwaiter().GetResult();
		var feed = _feed == null ? [] : Transform(_feed).ToArray();
		// to list so we actually fetch the query
		var toCheck = _db.Games.Include(g => g.Watchers).Where(dbGame => feed.Contains(dbGame)).ToArray();
		var toAdd = feed.Except(toCheck).ToList();
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
			_logger.ZLogInformation($"Adding [{string.Join(", ", toAdd)}] to database.");
			_db.Games.AddRange(toAdd);
		}

		if (toUpdate.Any())
		{
			_logger.ZLogInformation($"Updating games: {string.Join(", ", toUpdate)}");
			_db.UpdateRange(toUpdate);

			GamesUpdatedEvent?.Invoke(toUpdate);
		}

		_db.SaveChanges();
	}

	private async Task GetFeed()
	{
		try
		{
			// Set cookies if needed before request
			_cookieContainer.Add(new Uri(_config.RssFeedUrl), new Cookie("xf_user",    "277%2C3oE_qvfjALpH8Q2ddBRpuB5twhvX6qzfr65487Wv"));
			_cookieContainer.Add(new Uri(_config.RssFeedUrl), new Cookie("xf_session", "t4-o-i2R5Z_emHYFLx04DLsPMkskAj4y"));

			// Get feed stream with configured HttpClient
			await using var stream = await _httpClient.GetStreamAsync(_config.RssFeedUrl);
			using var reader = XmlReader.Create(stream);

			var feed = SyndicationFeed.Load(reader);
			_feed = feed;
		}
		catch (Exception e)
		{
			_logger.ZLogError(e, $"Failed to load RSS feed.");
		}
	}


	private static IEnumerable<Game> Transform(SyndicationFeed syndicationFeed)
	{
		foreach (var item in syndicationFeed.Items)
		{
			if (!item.Id.GetSanitizedUrl(out var sanitizedUrl)) continue;
			if (!sanitizedUrl.GetThreadId(out var threadId)) continue;
			yield return new Game { Title = item.Title.Text.HtmlDecode(), Url = sanitizedUrl, LastUpdated = item.PublishDate.DateTime, GameId = threadId };
		}
	}
}