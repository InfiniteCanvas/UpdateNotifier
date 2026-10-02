using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace UpdateNotifier.Utilities;

public static partial class StringUtilities
{
	private const RegexOptions _DEFAULT_COMPILED_ONCE_OPTIONS = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline;

	[GeneratedRegex(@"(https://f95zone.to/threads/)(.*?\.)?([0-9]+)/?", _DEFAULT_COMPILED_ONCE_OPTIONS, "en-US")]
	private static partial Regex ThreadRegex();

	public static bool GetSanitizedUrl(this string url, out string sanitizedUrl)
	{
		var match = ThreadRegex().Match(url);
		switch (match.Success)
		{
			case true:
				sanitizedUrl = $"{match.Groups[1]}{match.Groups[3]}";
				return true;
			case false:
				sanitizedUrl = string.Empty;
				return false;
		}
	}

	public static List<Result<Match?>> GetThreadPatternMatches(this string url)
	{
		var matches = ThreadRegex().Matches(url);
		return matches.Count switch
		{
			> 0 => matches.Select(match => match.Success ? new Result<Match?>(ResultStatus.Success, match) : Result<Match>.Failure())
			              .ToList(),
			_ => [],
		};
	}

	public static bool GetThreadId(this string url, out ulong threadId)
	{
		var match = ThreadRegex().Match(url);
		switch (match.Success)
		{
			case true:
				return ulong.TryParse(match.Groups[3].Value, out threadId);
			default:
				threadId = 0;
				return false;
		}
	}

	[GeneratedRegex(@"<img\b[^>]*?src\s*=\s*([""'])(?<url>https?://[^""']+)\1", _DEFAULT_COMPILED_ONCE_OPTIONS, "en-US")]
	private static partial Regex ThumbnailRegex();

	/// <summary>
	///     F95zone embeds each item's banner image as an HTML img tag inside the RSS description CDATA;
	///     this pulls the first http(s) src out of that snippet.
	/// </summary>
	public static bool GetThumbnailUrl(this string? html, out string thumbnailUrl)
	{
		var match = ThumbnailRegex().Match(html ?? string.Empty);
		if (!match.Success)
		{
			thumbnailUrl = string.Empty;
			return false;
		}

		thumbnailUrl = HttpUtility.HtmlDecode(match.Groups["url"].Value);
		return true;
	}

	public static MemoryStream StringToStream(this string s) => new(Encoding.UTF8.GetBytes(s));

	/// <summary>
	///     Account hashes are secrets (they authorize watchlist mutations); logs only ever see the first 8 characters.
	/// </summary>
	public static string RedactHash(this string hash) => $"{hash[..Math.Min(8, hash.Length)]}…";

	public static string ConvertToUtf8(this string s) => Encoding.UTF8.GetString(Encoding.Default.GetBytes(s));

	public static string HtmlDecode(this string s) => HttpUtility.HtmlDecode(s);
}