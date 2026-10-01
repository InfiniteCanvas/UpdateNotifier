using System.ComponentModel.DataAnnotations.Schema;

namespace UpdateNotifier.Data.Models;

[Table("Watchlist")]
public class WatchlistEntry
{
	public                        ulong UserId { get; set; }
	public                        ulong GameId { get; set; }
	[ForeignKey("GameId")] public Game? Game   { get; set; }
	[ForeignKey("UserId")] public User? User   { get; set; }
}