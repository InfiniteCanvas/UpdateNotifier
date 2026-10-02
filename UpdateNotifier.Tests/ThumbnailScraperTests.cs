using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Tests;

[Collection(DatabaseFixture.CollectionName)]
public sealed class ThumbnailScraperTests(DatabaseFixture fixture)
{
	// this class's own disjoint game id range so tests can share one database
	private const ulong GameIdBase = 9_000_000_000;

	private static readonly Uri PageUrl = new("https://f95zone.to/threads/137720");

	private const string AbsoluteSrcHtml = """
		<html>
			<body>
				<div class="lbContainer">
					<img src="https://attachments.f95zone.to/attachments/header-png.123/" />
				</div>
			</body>
		</html>
		""";

	private const string RelativeSrcHtml = """
		<html>
			<body>
				<div class="lbContainer">
					<img src="/attachments/header-png.123/" />
				</div>
			</body>
		</html>
		""";

	private const string DataSrcHtml = """
		<html>
			<body>
				<div class="lbContainer">
					<img data-src="https://attachments.f95zone.to/attachments/data-src-banner.png" />
				</div>
			</body>
		</html>
		""";

	private const string OgImageHtml = """
		<html>
			<head>
				<meta property="og:image" content="https://attachments.f95zone.to/og-image.png" />
			</head>
			<body>
				<p>No lightbox on this thread.</p>
			</body>
		</html>
		""";

	private const string BareHtml = """
		<html>
			<body>
				<p>Neither a lightbox container nor an og:image anywhere.</p>
			</body>
		</html>
		""";

	private static string? Extract(string html)
	{
		var document = new HtmlDocument();
		document.LoadHtml(html);
		return ThumbnailScraperService.ExtractThumbnailUrl(document, PageUrl);
	}

	private ThumbnailScraperService CreateScraper(DataContext db, StubHttpMessageHandler handler, ThumbnailQueue queue)
		=> new(NullLogger<ThumbnailScraperService>.Instance,
		       fixture.Services.GetRequiredService<Config>(),
		       db,
		       new StubHttpClientFactory(handler),
		       queue);

	private static async Task SeedGameAsync(DatabaseFixture fixture, ulong gameId, string? thumbnailUrl)
	{
		await using var db = fixture.CreateContext();
		db.Games.Add(new Game(gameId, $"Game {gameId}",
		                       new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
		                       $"https://f95zone.to/threads/{gameId}",
		                       thumbnailUrl));
		await db.SaveChangesAsync();
	}

	// ---- ExtractThumbnailUrl ------------------------------------------------------------------

	[Fact]
	public void ExtractThumbnailUrl_LbContainerAbsoluteSrc_ReturnedAsIs()
	{
		Assert.Equal("https://attachments.f95zone.to/attachments/header-png.123/", Extract(AbsoluteSrcHtml));
	}

	[Fact]
	public void ExtractThumbnailUrl_RelativeSrc_ResolvedAgainstPageUrl()
	{
		Assert.Equal("https://f95zone.to/attachments/header-png.123/", Extract(RelativeSrcHtml));
	}

	[Fact]
	public void ExtractThumbnailUrl_ImgWithOnlyDataSrc_UsesDataSrc()
	{
		Assert.Equal("https://attachments.f95zone.to/attachments/data-src-banner.png", Extract(DataSrcHtml));
	}

	[Fact]
	public void ExtractThumbnailUrl_NoLbContainer_FallsBackToOgImage()
	{
		Assert.Equal("https://attachments.f95zone.to/og-image.png", Extract(OgImageHtml));
	}

	[Fact]
	public void ExtractThumbnailUrl_NoImagesAnywhere_ReturnsNull()
	{
		Assert.Null(Extract(BareHtml));
	}

	[Fact]
	public void ExtractThumbnailUrl_SrcLongerThan255Chars_ReturnsNull()
	{
		var oversized = "<html><body><div class=\"lbContainer\"><img src=\"https://attachments.f95zone.to/"
		                + new string('a', 300) + "\" /></div></body></html>";

		Assert.Null(Extract(oversized));
	}

	// ---- ProcessItemAsync ---------------------------------------------------------------------

	[Fact]
	public async Task ProcessItemAsync_ThumbnaillessGame_FetchesAndPersistsThumbnail()
	{
		const ulong gameId = GameIdBase + 1;
		await SeedGameAsync(fixture, gameId, null);
		var handler = new StubHttpMessageHandler(_ => AbsoluteSrcHtml);

		await using (var db = fixture.CreateContext())
		{
			var scraper = CreateScraper(db, handler, new ThumbnailQueue());
			await scraper.ProcessItemAsync(gameId, CancellationToken.None);
		}

		Assert.Equal($"https://f95zone.to/threads/{gameId}", Assert.Single(handler.RequestedUrls));
		await using var verify = fixture.CreateContext();
		Assert.Equal("https://attachments.f95zone.to/attachments/header-png.123/",
		             await verify.Games.Where(g => g.GameId == gameId).Select(g => g.ThumbnailUrl).SingleAsync());
	}

	[Fact]
	public async Task ProcessItemAsync_GameWithExistingThumbnail_SkipsFetchAndKeepsThumbnail()
	{
		const ulong gameId = GameIdBase + 2;
		const string existing = "https://example.com/banners/existing.png";
		await SeedGameAsync(fixture, gameId, existing);
		var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("A game with a thumbnail must not be fetched."));

		await using (var db = fixture.CreateContext())
		{
			var scraper = CreateScraper(db, handler, new ThumbnailQueue());
			await scraper.ProcessItemAsync(gameId, CancellationToken.None);
		}

		Assert.Empty(handler.RequestedUrls);
		await using var verify = fixture.CreateContext();
		Assert.Equal(existing, await verify.Games.Where(g => g.GameId == gameId).Select(g => g.ThumbnailUrl).SingleAsync());
	}

	[Fact]
	public async Task ProcessItemAsync_PageWithoutUsableImage_LeavesThumbnailNull()
	{
		const ulong gameId = GameIdBase + 3;
		await SeedGameAsync(fixture, gameId, null);
		var handler = new StubHttpMessageHandler(_ => BareHtml);

		await using (var db = fixture.CreateContext())
		{
			var scraper = CreateScraper(db, handler, new ThumbnailQueue());
			await scraper.ProcessItemAsync(gameId, CancellationToken.None);
		}

		Assert.Equal($"https://f95zone.to/threads/{gameId}", Assert.Single(handler.RequestedUrls));
		await using var verify = fixture.CreateContext();
		Assert.Null(await verify.Games.Where(g => g.GameId == gameId).Select(g => g.ThumbnailUrl).SingleAsync());
	}
}
