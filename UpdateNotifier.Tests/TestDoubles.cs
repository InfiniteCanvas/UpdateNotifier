using System.Net;
using System.Text;
using System.Xml;
using System.ServiceModel.Syndication;
using UpdateNotifier.Abstractions;

namespace UpdateNotifier.Tests;

/// <summary>
///     Fails loudly if any code path tries to make HTTP traffic during a test.
///     GameInfoProvider fetches thread pages for games not yet in the database, so tests
///     pre-seed those rows instead.
/// </summary>
public sealed class ThrowingHttpClientFactory : IHttpClientFactory
{
	public static readonly ThrowingHttpClientFactory Instance = new();

	public HttpClient CreateClient(string name)
		=> throw new InvalidOperationException("No HTTP traffic is expected in these tests.");
}

/// <summary>
///     Serves canned HTML instead of reaching the network: every requested URL is recorded so
///     tests can assert what was (or was not) fetched, and the responder decides the body.
/// </summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, string> responder) : HttpMessageHandler
{
	public List<string> RequestedUrls { get; } = [];

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		RequestedUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(responder(request), Encoding.UTF8, "text/html")
		});
	}
}

public sealed class StubHttpClientFactory(StubHttpMessageHandler handler) : IHttpClientFactory
{
	public HttpClient CreateClient(string name)
		=> name == "F95Thread"
			? new HttpClient(handler) { BaseAddress = new Uri("https://f95zone.to/") }
			: throw new InvalidOperationException("No HTTP traffic is expected in these tests.");
}

/// <summary>
///     A clock that only moves when the test moves it - deterministic refill math for rate limiters.
/// </summary>
public sealed class MutableTimeProvider : TimeProvider
{
	public DateTimeOffset NowValue { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	public override DateTimeOffset GetUtcNow()
		=> NowValue;
}

public sealed class FakeDmSender : IDmSender
{
	private readonly List<(ulong UserId, string Message)> _sends = [];

	public IReadOnlyList<(ulong UserId, string Message)> Sends => _sends;

	public ValueTask SendDmAsync(ulong userId, string message, CancellationToken ct = default)
	{
		_sends.Add((userId, message));
		return ValueTask.CompletedTask;
	}
}

/// <summary>
///     Throws on the very first send and succeeds afterwards - proves a failing DM must not
///     kill the notification loop (and with it, via StopHost, the whole host).
/// </summary>
public sealed class FlakyDmSender : IDmSender
{
	private int _calls;

	public int Calls => Volatile.Read(ref _calls);

	public List<(ulong UserId, string Message)> Sends { get; } = [];

	public ValueTask SendDmAsync(ulong userId, string message, CancellationToken ct = default)
	{
		var call = Interlocked.Increment(ref _calls);
		if (call == 1) throw new InvalidOperationException("Simulated DM failure.");
		Sends.Add((userId, message));
		return ValueTask.CompletedTask;
	}
}

public sealed class FakePrivilegeChecker(bool privileged) : IPrivilegeChecker
{
	public ValueTask<bool> IsPrivilegedAsync(ulong userId, CancellationToken ct = default)
		=> ValueTask.FromResult(privileged);
}

public static class RssFeed
{
	public static SyndicationFeed MakeFeed(params (ulong GameId, string Title, DateTime Updated, string? Thumbnail)[] items)
	{
		var builder = new StringBuilder();
		builder.Append("""
		               <?xml version="1.0" encoding="utf-8"?>
		               <rss version="2.0">
		                 <channel>
		                   <title>UpdateNotifier Tests</title>
		                   <link>https://f95zone.to/</link>
		                   <description>Fixture feed</description>
		               """);
		foreach (var (gameId, title, updated, thumbnail) in items)
		{
			// CDATA shields the img tag from the XML parser, so the URL needs no XML escaping.
			var description = thumbnail is null ? string.Empty : $"<description><![CDATA[<img src=\"{thumbnail}\" alt=\"\" />]]></description>";
			builder.Append($"""
			                    <item>
			                      <title>{title}</title>
			                      {description}
			                      <link>https://f95zone.to/threads/{gameId}/</link>
			                      <guid>https://f95zone.to/threads/thread.{gameId}/</guid>
			                      <pubDate>{updated:R}</pubDate>
			                    </item>
			                    """);
		}
		builder.Append("""
		                 </channel>
		               </rss>
		               """);

		using var reader = XmlReader.Create(new StringReader(builder.ToString()));
		return SyndicationFeed.Load(reader);
	}
}
