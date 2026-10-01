using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UpdateNotifier.Data.Models;

/// <summary>
///     Website login session linked to an <see cref="Account" />. Created by the schema now;
///     the web frontend arrives in a later batch.
/// </summary>
[Table("WebSessions")]
public class WebSession
{
	[Key] public string TokenHash { get; set; } = null!;

	public ulong AccountId { get; set; }

	[ForeignKey("AccountId")] public Account? Account { get; set; }

	public DateTime CreatedAt { get; set; }

	public DateTime ExpiresAt { get; set; }
}
