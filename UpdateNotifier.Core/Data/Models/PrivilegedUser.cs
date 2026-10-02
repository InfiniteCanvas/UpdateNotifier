using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UpdateNotifier.Data.Models;

/// <summary>
///     Cache of Discord users with elevated privileges (supporter-tier watchlist limit bypass).
///     Replaced wholesale by the bot container over the internal API and read by
///     <see cref="UpdateNotifier.Abstractions.SyncedPrivilegeChecker" />.
/// </summary>
[Table("PrivilegedUsers")]
public class PrivilegedUser(ulong userId)
{
	[Key] public ulong UserId { get; set; } = userId;

	public override string ToString() => $"{nameof(UserId)}: {UserId}";
}
