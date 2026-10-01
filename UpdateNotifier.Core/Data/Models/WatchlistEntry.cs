using System.ComponentModel.DataAnnotations.Schema;

namespace UpdateNotifier.Data.Models;

[Table("Watchlist")]
public class WatchlistEntry
{
	public                        ulong     AccountId { get; set; }
	public                        ulong     GameId    { get; set; }
	[ForeignKey("GameId")] public Game?    Game      { get; set; }
	[ForeignKey("AccountId")] public Account? Account  { get; set; }
}