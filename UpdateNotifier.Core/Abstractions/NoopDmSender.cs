using Microsoft.Extensions.Logging;
using UpdateNotifier.Abstractions;
using ZLogger;

namespace UpdateNotifier.Abstractions;

public sealed class NoopDmSender(ILogger<NoopDmSender> logger) : IDmSender
{
	public ValueTask SendDmAsync(ulong userId, string message, CancellationToken ct = default)
	{
		logger.ZLogDebug($"Running headless, not sending DM to {userId}: {message}");
		return ValueTask.CompletedTask;
	}
}
