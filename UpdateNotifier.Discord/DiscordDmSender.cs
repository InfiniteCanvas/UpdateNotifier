using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Abstractions;
using ZLogger;

namespace UpdateNotifier.Bot;

public sealed class DiscordDmSender(DiscordSocketClient client, ILogger<DiscordDmSender> logger) : IDmSender
{
	public async ValueTask SendDmAsync(ulong userId, string message, CancellationToken ct = default)
	{
		var user = await client.GetUserAsync(userId, new RequestOptions { CancelToken = ct });
		if (user == null)
		{
			logger.ZLogWarning($"Cannot send notification to {userId}: the user could not be resolved.");
			return;
		}

		logger.ZLogInformation($"Sending notification to {user.Username}: {message}");
		await user.SendMessageAsync(message, options: new RequestOptions { CancelToken = ct });
	}
}
