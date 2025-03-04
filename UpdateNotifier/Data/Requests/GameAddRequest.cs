namespace UpdateNotifier.Data.Requests;

public class GameAddRequest
{
	public string   ThreadUrl;
	public string   ThreadTitle;
	public DateTime LastUpdate;
	public ulong    UserHash;
}