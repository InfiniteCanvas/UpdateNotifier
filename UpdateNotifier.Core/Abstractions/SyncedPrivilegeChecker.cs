using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Abstractions;

/// <summary>
///     Answers privilege checks from the <see cref="PrivilegedUser" /> cache the bot container pushes
///     over the internal API; self-hosted instances treat everyone as privileged, like the inline
///     checker did.
/// </summary>
public sealed class SyncedPrivilegeChecker(IServiceProvider services, Config config, ILogger<SyncedPrivilegeChecker> logger) : IPrivilegeChecker
{
	public async ValueTask<bool> IsPrivilegedAsync(ulong userId, CancellationToken ct = default)
	{
		if (config.SelfHosted)
		{
			logger.ZLogDebug($"Privileged because Self hosted");
			return true;
		}

		try
		{
			// this checker is a singleton while DataContext is transient: scope per lookup
			using var scope = services.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<DataContext>();
			return await db.PrivilegedUsers.AnyAsync(u => u.UserId == userId, ct);
		}
		catch (Exception e)
		{
			// fail closed: a broken cache must not hand out the privileged limit for free
			logger.ZLogError(e, $"Error checking privilege for user {userId}");
			return false;
		}
	}
}
