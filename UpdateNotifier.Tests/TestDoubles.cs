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

public sealed class FakePrivilegeChecker(bool privileged) : IPrivilegeChecker
{
	public ValueTask<bool> IsPrivilegedAsync(ulong userId, CancellationToken ct = default)
		=> ValueTask.FromResult(privileged);
}

public static class RssFeed
{
	public static SyndicationFeed MakeFeed(params (ulong GameId, string Title, DateTime Updated)[] items)
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
		foreach (var (gameId, title, updated) in items)
			builder.Append($"""
			                    <item>
			                      <title>{title}</title>
			                      <link>https://f95zone.to/threads/{gameId}/</link>
			                      <guid>https://f95zone.to/threads/thread.{gameId}/</guid>
			                      <pubDate>{updated:R}</pubDate>
			                    </item>
			                    """);
		builder.Append("""
		                 </channel>
		               </rss>
		               """);

		using var reader = XmlReader.Create(new StringReader(builder.ToString()));
		return SyndicationFeed.Load(reader);
	}
}
