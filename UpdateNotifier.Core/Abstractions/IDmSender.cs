namespace UpdateNotifier.Abstractions;

/// <summary>
///     Sends a direct message to a user. Implemented by the Discord bot; a no-op in headless mode.
/// </summary>
public interface IDmSender
{
	public ValueTask SendDmAsync(ulong userId, string message, CancellationToken ct = default);
}
