using Discord;

namespace UpdateNotifier.Utilities;

/// <summary>
///     The one-time privacy notice shown after an interaction implicitly created the user's account.
/// </summary>
public static class PrivacyNotice
{
	public static Embed Build() => new EmbedBuilder()
		.WithTitle("Data & Privacy")
		.WithDescription("By tracking games you agree that your Discord user ID is stored to manage your watchlist "
		                + "and to send you DM notifications when games update (these may contain NSFW content).\n"
		                + "You can delete all of your data at any time using the `/disable` command.\n"
		                + "Your data is never shared with third parties.\n"
		                + "Optionally, you can link your account to the website at https://bot.infinitecanvas.io/ "
		                + "at any time using `/link`.")
		.WithColor(Color.Blue)
		.WithFooter("UpdateNotifier Bot")
		.WithCurrentTimestamp()
		.Build();
}
