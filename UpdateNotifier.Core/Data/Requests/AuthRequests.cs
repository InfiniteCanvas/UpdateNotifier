namespace UpdateNotifier.Data.Requests;

/// <summary>Register/login request body: {username, password}. Deliberately has no ToString - it carries a password.</summary>
public class AuthCredentialsRequest
{
	public string Username { get; set; } = string.Empty;

	public string Password { get; set; } = string.Empty;
}

/// <summary>Account deletion request body: {password}.</summary>
public class PasswordRequest
{
	public string Password { get; set; } = string.Empty;
}
