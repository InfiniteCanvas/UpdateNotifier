using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UpdateNotifier.Bot;
using UpdateNotifier.Communication;

namespace UpdateNotifier.Services;

// botConfig is unread: the base address and API key are applied by the AddHttpClient registration,
// the parameter is kept so the client carries its own configuration shape
#pragma warning disable CS9113

/// <summary>
///     The API answered 404: the Discord user has no account yet; the bot auto-registers one on first /watch or /link.
///     Carries the server's user-facing message from its {"error": ...} body.
/// </summary>
public sealed class UserNotFoundException(string message) : Exception(message);

/// <summary>
///     Typed client for the api container's /api/internal endpoints. The bot process has no
///     database access: everything it used to do via DataContext/IWebAuthService goes through here.
/// </summary>
public sealed class UpdateNotifierApiClient(HttpClient httpClient, BotConfig botConfig)
{
	public async Task<IReadOnlyList<PendingNotificationDto>> GetPendingAsync(int limit = 50, CancellationToken ct = default)
	{
		var response = await httpClient.GetAsync($"/api/internal/notifications/pending?limit={limit}", ct);
		await EnsureSuccessAsync(response, ct);
		return await ReadResponseAsync<IReadOnlyList<PendingNotificationDto>>(response, ct);
	}

	public async Task AckAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
	{
		var response = await httpClient.PostAsJsonAsync("/api/internal/notifications/ack", new AckRequest([.. ids]), ct);
		await EnsureSuccessAsync(response, ct);
	}

	public async Task SyncPrivilegesAsync(IReadOnlyCollection<ulong> userIds, CancellationToken ct = default)
	{
		var response = await httpClient.PutAsJsonAsync("/api/internal/privileges", new PrivilegeSyncRequest([.. userIds]), ct);
		await EnsureSuccessAsync(response, ct);
	}

	public async Task<InternalUserDto> GetUserAsync(ulong discordId, CancellationToken ct = default)
	{
		var response = await httpClient.GetAsync($"/api/internal/users/{discordId}", ct);
		await EnsureSuccessAsync(response, ct);
		return await ReadResponseAsync<InternalUserDto>(response, ct);
	}

	public async Task<bool> AddUserAsync(ulong discordId, string username, CancellationToken ct = default)
	{
		var response = await httpClient.PostAsJsonAsync("/api/internal/users", new AddUserRequest(discordId, username), ct);
		await EnsureSuccessAsync(response, ct);
		return await ReadResponseAsync<bool>(response, ct);
	}

	public async Task<bool> RemoveUserAsync(ulong discordId, CancellationToken ct = default)
	{
		var response = await httpClient.DeleteAsync($"/api/internal/users/{discordId}", ct);
		await EnsureSuccessAsync(response, ct);
		return await ReadResponseAsync<bool>(response, ct);
	}

	public async Task<WatchResponse> WatchAsync(ulong discordId, IReadOnlyList<string> urls, bool privileged, CancellationToken ct = default)
	{
		var response = await httpClient.PostAsJsonAsync("/api/internal/watch", new WatchRequest(discordId, [.. urls], privileged), ct);
		await EnsureSuccessAsync(response, ct);
		return await ReadResponseAsync<WatchResponse>(response, ct);
	}

	public async Task<WatchResponse> UnwatchAsync(ulong discordId, IReadOnlyList<string> urls, bool privileged, CancellationToken ct = default)
	{
		var response = await httpClient.PostAsJsonAsync("/api/internal/unwatch", new WatchRequest(discordId, [.. urls], privileged), ct);
		await EnsureSuccessAsync(response, ct);
		return await ReadResponseAsync<WatchResponse>(response, ct);
	}

	public async Task<IReadOnlyList<InternalGameDto>> GetWatchlistAsync(ulong discordId, CancellationToken ct = default)
	{
		var response = await httpClient.GetAsync($"/api/internal/watch/{discordId}", ct);
		await EnsureSuccessAsync(response, ct);
		return await ReadResponseAsync<IReadOnlyList<InternalGameDto>>(response, ct);
	}

	public async Task<LinkConsumeResponse> ConsumeLinkCodeAsync(string code, ulong discordId, string username, CancellationToken ct = default)
	{
		var response = await httpClient.PostAsJsonAsync("/api/internal/link/consume", new LinkConsumeRequest(code, discordId, username), ct);
		await EnsureSuccessAsync(response, ct);
		return await ReadResponseAsync<LinkConsumeResponse>(response, ct);
	}

	/// <summary>Maps 404s to <see cref="UserNotFoundException" />; every other non-success stays an HttpRequestException.</summary>
	private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
	{
		if (response.IsSuccessStatusCode) return;

		if (response.StatusCode == HttpStatusCode.NotFound)
			throw new UserNotFoundException(await ReadErrorMessageAsync(response, ct));

		response.EnsureSuccessStatusCode();
	}

	/// <summary>The server's {"error": ...} message, or a status-code fallback when the body is not the expected shape.</summary>
	private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken ct)
	{
		try
		{
			await using var stream = await response.Content.ReadAsStreamAsync(ct);
			using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
			if (document.RootElement.TryGetProperty("error", out var error) && error.GetString() is { } message)
				return message;
		}
		catch (JsonException)
		{
			// not a JSON error body - fall through to the status-code fallback
		}

		return $"HTTP {(int) response.StatusCode} {response.ReasonPhrase}";
	}

	private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken ct)
		=> await response.Content.ReadFromJsonAsync<T>(ct)
		   ?? throw new HttpRequestException($"The API returned a null {typeof(T).Name} body.");
}
