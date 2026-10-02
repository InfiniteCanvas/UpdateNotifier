using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UpdateNotifier.Data.Models;

/// <summary>
///     A durable DM produced by the notification pipeline: the API enqueues it, the bot container
///     polls it over the internal API and acks it once delivered. Rows that exhaust their delivery
///     attempts are dead-lettered (<see cref="PendingNotificationStatus.Dead" />).
/// </summary>
[Table("PendingNotifications")]
public class PendingNotification(ulong discordUserId, string message)
{
	[Key] public long Id { get; set; }

	public ulong DiscordUserId { get; init; } = discordUserId;

	public string Message { get; init; } = message;

	/// <summary>UTC timestamp of the enqueue.</summary>
	public DateTime CreatedAt { get; init; }

	/// <summary>Polls that claimed this row; once past the dead-letter threshold it never returns again.</summary>
	public int Attempts { get; set; }

	public PendingNotificationStatus Status { get; set; } = PendingNotificationStatus.Pending;

	public override string ToString() => $"{nameof(Id)}: {Id}, {nameof(DiscordUserId)}: {DiscordUserId}, {nameof(Status)}: {Status}";
}

/// <summary>Lifecycle of a <see cref="PendingNotification" /> row.</summary>
public enum PendingNotificationStatus
{
	/// <summary>Waiting to be fetched by the bot container.</summary>
	Pending = 0,

	/// <summary>Delivered and acknowledged by the bot container.</summary>
	Sent = 1,

	/// <summary>Exhausted its delivery attempts; kept for auditing, never delivered again.</summary>
	Dead = 2
}
