using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Services;

public partial class GameInfoProvider(ILogger<GameInfoProvider> logger, IHttpClientFactory clientFactory) : IDisposable
{
	private const RegexOptions _DEFAULT_COMPILED_ONCE_OPTIONS = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline;

	private readonly SemaphoreSlim _semaphoreSlim = new(5, 5);
	private readonly Regex         _title         = TitleRegex();
	private readonly Regex         _updated       = UpdatedRegex();

	public void Dispose()
	{
		_semaphoreSlim.Dispose();
		GC.SuppressFinalize(this);
	}

	[GeneratedRegex(@"<title>(.*) ?\| F95zone<\/title>", _DEFAULT_COMPILED_ONCE_OPTIONS, "en-US")]
	private static partial Regex TitleRegex();

	[GeneratedRegex(@"<b>[Tt]hread [Uu]pdated?[<\/b>: ]+([0-9-]+)<br", _DEFAULT_COMPILED_ONCE_OPTIONS, "en-US")]
	private static partial Regex UpdatedRegex();

	public async Task<Game> GetGameInfo(string url)
	{
		var client = clientFactory.CreateClient();
		await _semaphoreSlim.WaitAsync();
		try
		{
			var content = await client.GetStringAsync(url);
			var title = _title.Match(content).Groups[1].Value.HtmlDecode();
			var updatedMatch = _updated.Match(content).Groups[1];
			var updated = updatedMatch.Success switch
			{
				true  => updatedMatch.Value,
				false => "2000-01-01",
			};
			// just pray it works lmao
			url.GetThreadId(out var gameId);
			var game = new Game { Title = title, LastUpdated = DateTime.Parse(updated), Url = url, GameId = gameId };
			logger.ZLogDebug($"Getting info on {url}: {game}");
			return game;
		}
		finally
		{
			_semaphoreSlim.Release();
		}
	}
}