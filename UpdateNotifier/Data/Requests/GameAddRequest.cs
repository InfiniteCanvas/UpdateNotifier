namespace UpdateNotifier.Data.Requests;

public class GameAddRequest
{
	public DateTime LastUpdate;
	public string   ThreadTitle;
	public string   ThreadUrl;
	public ulong    UserHash;
}