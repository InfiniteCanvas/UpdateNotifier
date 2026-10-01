using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UpdateNotifier.Data.Models;

[Table("Accounts")]
public class Account
{
	[Key] public ulong AccountId { get; set; }

	[MaxLength(40)] public string Hash { get; set; } = null!;

	public DateTime CreatedAt { get; set; }

	[NotMapped] public List<Game> Games { get; set; } = [];

	/// <summary>The linked Discord identity, if the account was created via the bot.</summary>
	public User? User { get; set; }

	public override string ToString() => $"{nameof(AccountId)}: {AccountId}";
}
