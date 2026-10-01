using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UpdateNotifier.Data.Models;

/// <summary>
///     Website login credential linked to an <see cref="Account" />. Created by the schema now;
///     the web frontend arrives in a later batch.
/// </summary>
[Table("WebAccounts")]
public class WebAccount
{
	[Key] public int WebAccountId { get; set; }

	[MaxLength(32)] public string Username { get; set; } = null!;

	public string PasswordHash { get; set; } = null!;

	public ulong AccountId { get; set; }

	[ForeignKey("AccountId")] public Account? Account { get; set; }
}
