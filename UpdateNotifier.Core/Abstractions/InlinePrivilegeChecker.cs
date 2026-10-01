namespace UpdateNotifier.Abstractions;

public sealed class InlinePrivilegeChecker(bool privileged) : IPrivilegeChecker
{
	public ValueTask<bool> IsPrivilegedAsync(ulong userId, CancellationToken ct = default)
		=> ValueTask.FromResult(privileged);
}
