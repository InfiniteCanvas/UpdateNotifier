using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Services;

/// <summary>
///     Lazily fills in thumbnails for games the RSS feed never provided one for: pulls each thread
///     page (token-bucket throttled, default 30/minute) and takes the banner from its first
///     lightbox container. Priority comes from <see cref="ThumbnailQueue" /> — watchlist views jump
///     the line, housekeeping sweeps the rest. Persistence is a bare ExecuteUpdate: it must never
///     fire GamesUpdatedEvent, or every scraped thumbnail would DM people.
/// </summary>
public sealed class ThumbnailScraperService(
	ILogger<ThumbnailScraperService> logger,
	Config             config,
	DataContext        db,
	IHttpClientFactory httpClientFactory,
	ThumbnailQueue     queue)
	: BackgroundService
{
	// small burst allowance: an idle bucket must never dump a full minute of requests onto f95zone at once
	private const int BurstCapacity = 5;

	private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(1);

	private readonly TokenBucket _bucket = new(BurstCapacity, config.ThumbnailScrapeRatePerMinute / 60.0);

	// the sweep and the processing loop share this DbContext: EF contexts are not thread-safe, so the
	// loop drives both from one thread instead of subscribing a timer callback alongside itself
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logger.ZLogInformation($"ThumbnailScraperService is starting.");
		var lastSweep = DateTimeOffset.MinValue; // sweep as soon as the loop starts
		while (!stoppingToken.IsCancellationRequested)
		{
			ulong gameId = 0;
			try
			{
				if (DateTimeOffset.UtcNow - lastSweep >= config.ThumbnailSweepInterval)
				{
					await SweepHousekeeping(stoppingToken);
					lastSweep = DateTimeOffset.UtcNow;
				}

				if (!queue.TryDequeue(out gameId))
				{
					await Task.Delay(IdlePollInterval, stoppingToken);
					continue;
				}

				await _bucket.WaitForTokenAsync(stoppingToken);
				await ProcessItemAsync(gameId, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception e)
			{
				// BackgroundServiceExceptionBehavior.StopHost: one bad page must never take the API down
				logger.ZLogError(e, $"ThumbnailScraperService failed to process game [{gameId}]");
				if (gameId != 0) queue.MarkFailed(gameId);
			}
		}
	}

	internal async Task ProcessItemAsync(ulong gameId, CancellationToken ct)
	{
		// the row may have gained a thumbnail via the RSS backfill between enqueue and now
		var url = await db.Games.AsNoTracking()
		                  .Where(g => g.GameId == gameId && g.ThumbnailUrl == null)
		                  .Select(g => g.Url)
		                  .FirstOrDefaultAsync(ct);
		if (url is null) return;

		// created per item (like RssMonitorService) so the factory's handler rotation keeps DNS fresh
		var client = httpClientFactory.CreateClient("F95Thread");
		var html = await client.GetStringAsync(url, ct);
		var document = new HtmlDocument();
		document.LoadHtml(html);
		var thumbnail = ExtractThumbnailUrl(document, new Uri(url));
		if (thumbnail is null)
		{
			queue.MarkFailed(gameId);
			logger.ZLogWarning($"No usable thumbnail found on {url}.");
			return;
		}

		await db.Games.Where(g => g.GameId == gameId)
		        .ExecuteUpdateAsync(s => s.SetProperty(g => g.ThumbnailUrl, thumbnail), ct);
		logger.ZLogInformation($"Scraped thumbnail for game [{gameId}]: {thumbnail}");
	}

	/// <summary>
	///     The banner lives in the first INLINE lightbox (lbContainer--inline — its title attribute names
	///     the header file). The bare lbContainer token is not specific enough: XenForo also puts it on
	///     whole-post and whole-thread wrappers (message-userContent, the thread block itself), whose
	///     first descendant img is the poster's avatar. XenForo lazy-loading can pair a placeholder src
	///     with the real URL in data-src, so both are tried before falling back to og:image.
	/// </summary>
	internal static string? ExtractThumbnailUrl(HtmlDocument document, Uri pageUrl)
	{
		var container = document.DocumentNode.SelectSingleNode("//div[contains(concat(' ', normalize-space(@class), ' '), ' lbContainer--inline ')]");
		var img = container?.SelectSingleNode(".//img");
		var candidates = new[]
		{
			img?.GetAttributeValue("src", null),
			img?.GetAttributeValue("data-src", null),
			document.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", null)
		};
		foreach (var candidate in candidates)
			if (TryResolveImageUrl(candidate, pageUrl, out var url))
				return url;
		return null;
	}

	private async Task SweepHousekeeping(CancellationToken ct)
	{
		try
		{
			var missing = await db.Games.AsNoTracking()
			                      .Where(g => g.ThumbnailUrl == null)
			                      .Select(g => new { g.GameId, g.LastUpdated })
			                      .ToListAsync(ct);
			if (missing.Count == 0) return;
			queue.EnqueueHousekeeping(missing.Select(m => (m.GameId, m.LastUpdated)));
			logger.ZLogDebug($"Thumbnail housekeeping enqueued {missing.Count} games without a thumbnail.");
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"Thumbnail housekeeping sweep failed.");
		}
	}

	private static bool TryResolveImageUrl(string? src, Uri pageUrl, out string url)
	{
		url = string.Empty;
		if (string.IsNullOrWhiteSpace(src) || src.Length > 255) return false;
		if (Uri.TryCreate(src, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
		{
			url = absolute.ToString();
		}
		else if (Uri.TryCreate(src, UriKind.Relative, out var relative))
		{
			var resolved = new Uri(pageUrl, relative);
			if (resolved.Scheme is not ("http" or "https") || resolved.ToString().Length > 255) return false;
			url = resolved.ToString();
		}
		else
		{
			return false;
		}

		// avatars live under /data/avatars/ and are never the game banner
		if (url.Contains("/data/avatars/", StringComparison.OrdinalIgnoreCase))
		{
			url = string.Empty;
			return false;
		}

		return true;
	}
}
