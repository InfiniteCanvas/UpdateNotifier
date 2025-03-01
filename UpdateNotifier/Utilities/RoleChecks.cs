using Discord.WebSocket;

namespace UpdateNotifier.Utilities;

public static class RoleChecks
{
	private static readonly ulong _supporter    = 1345449839801925692;
	private static readonly ulong _tester       = 1021813826938748979;
	private static readonly bool  _isSelfHosted = Environment.GetEnvironmentVariable("IS_SELF_HOST") == "true";

	public static bool IsPrivileged(this SocketGuildUser user) => _isSelfHosted || user.Roles.Any(r => r.Id == _supporter || r.Id == _tester);
}