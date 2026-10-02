using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Models;
using ZLogger;

namespace UpdateNotifier.Abstractions;

/// <summary>
///     Enqueues DMs as durable <see cref="PendingNotification" /> rows; the bot container polls them
///     over the internal API and delivers them. A failed enqueue is logged but never thrown - the
///     notification pipeline must survive it (same contract as <see cref="NoopDmSender" />).
/// </summary>
public sealed class QueuedDmSender(IServiceProvider services, ILogger<QueuedDmSender> logger) : IDmSender
{
	public async ValueTask SendDmAsync(ulong userId, string message, CancellationToken ct = default)
	{
		try
		{
			// this sender is a singleton while DataContext is transient: scope per enqueue
			using var scope = services.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<DataContext>();
			db.PendingNotifications.Add(new PendingNotification(userId, message) { CreatedAt = DateTime.UtcNow });
			await db.SaveChangesAsync(ct);
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"Failed to enqueue notification for user {userId}");
		}
	}
}
