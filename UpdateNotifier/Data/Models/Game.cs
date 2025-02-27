namespace UpdateNotifier.Data.Models;

public class Game
{
	public ulong           GameId      { get; set; }
	public string          Title       { get; set; }
	public DateTime        LastUpdated { get; set; }
	public string          Url         { get; set; }
	public List<Watchlist> Watchers    { get; set; }
}