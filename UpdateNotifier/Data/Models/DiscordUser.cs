namespace UpdateNotifier.Data.Models;

public class DiscordUser
{
	public ulong           UserId    { get; set; }
	public List<Watchlist> Watchlist { get; set; }
}