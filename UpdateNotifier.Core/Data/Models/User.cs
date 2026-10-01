using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UpdateNotifier.Data.Models;

/// <summary>
///     A Discord identity linked to an <see cref="Account" />. The watchlist and the extension
///     hash live on the account; this entity is only the snowflake -> account link.
/// </summary>
[Table("Users")]
public class User(ulong userId)
{
	[Key] public ulong UserId { get; set; } = userId;

	[MaxLength(64)] public string? DiscordUsername { get; set; }

	public ulong AccountId { get; set; }

	[ForeignKey("AccountId")] public Account? Account { get; set; }

	public override string ToString() => $"{nameof(UserId)}: {UserId}";
}