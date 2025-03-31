namespace UpdateNotifier.Data.Requests;

public class GameAddRequest
{
	public string ThreadUrl { get; set; }
	public string UserHash  { get; set; }

	public override string ToString() => $"{nameof(ThreadUrl)}: {ThreadUrl}, {nameof(UserHash)}: {UserHash}";
}