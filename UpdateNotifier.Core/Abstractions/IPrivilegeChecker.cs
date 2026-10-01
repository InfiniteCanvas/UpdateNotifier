namespace UpdateNotifier.Abstractions;

/// <summary>
///     Checks whether a user has elevated privileges (supporter/self-hosted bypass of the free watchlist limit).
/// </summary>
public interface IPrivilegeChecker
{
	public ValueTask<bool> IsPrivilegedAsync(ulong userId, CancellationToken ct = default);
}
