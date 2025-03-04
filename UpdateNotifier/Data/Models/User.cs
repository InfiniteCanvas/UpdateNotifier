using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UpdateNotifier.Data.Models;

[Table("Users")]
public class User(ulong userId)
{
	[Key] public ulong UserId { get; set; } = userId;

	[DatabaseGenerated(DatabaseGeneratedOption.Computed)]
	public string Hash { get; private set; } = null!;

	[NotMapped] public List<Game> Games { get; set; } = [];

	public override string ToString() => $"{nameof(UserId)}: {UserId}";
}