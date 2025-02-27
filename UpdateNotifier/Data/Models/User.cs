using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UpdateNotifier.Data.Models;

[Table("Users")]
public class User
{
	[Key]       public ulong      UserId { get; set; }
	[NotMapped] public List<Game> Games  { get; set; } = [];

	public override string ToString() => $"{nameof(UserId)}: {UserId}";
}