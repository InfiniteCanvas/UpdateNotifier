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

	public static MemoryStream StringToStream(this string s) => new(Encoding.UTF8.GetBytes(s));

	public static string ConvertToUtf8(this string s) => Encoding.UTF8.GetString(Encoding.Default.GetBytes(s));

	public static string HtmlDecode(this string s) => HttpUtility.HtmlDecode(s);
}