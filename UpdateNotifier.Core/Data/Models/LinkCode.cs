using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UpdateNotifier.Data.Models;

/// <summary>
///     Short-lived code that links a website login to an existing <see cref="Account" />.
///     Created by the schema now; the linking flow arrives in a later batch.
/// </summary>
[Table("LinkCodes")]
public class LinkCode
{
	[Key] public string Code { get; set; } = null!;

	public ulong AccountId { get; set; }

	[ForeignKey("AccountId")] public Account? Account { get; set; }

	public DateTime CreatedAt { get; set; }

	public DateTime ExpiresAt { get; set; }
}
