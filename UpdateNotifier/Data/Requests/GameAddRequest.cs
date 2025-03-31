using Newtonsoft.Json;

namespace UpdateNotifier.Data.Requests;

public class GameAddRequest
{
	[JsonRequired] public string ThreadUrl           { get; set; } = string.Empty;
	[JsonRequired] public string UserHash            { get; set; } = string.Empty;
	public                bool   DiscordNotification { get; set; } = false;

	public override string ToString() => $"{nameof(ThreadUrl)}: {ThreadUrl}, {nameof(UserHash)}: {UserHash}";
}