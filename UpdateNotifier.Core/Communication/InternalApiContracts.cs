using UpdateNotifier.Services;

namespace UpdateNotifier.Communication;

// Request/response records for the /api/internal endpoints - the contract between the API
// container and the bot container. Consumed by the bot over HTTP, so every name and shape here
// is public API and must not change casually.

/// <summary>A queued notification the bot container should deliver as a Discord DM.</summary>
public sealed record PendingNotificationDto(long Id, ulong DiscordUserId, string Message);

/// <summary>Body of POST /api/internal/notifications/ack: the ids the bot successfully delivered.</summary>
public sealed record AckRequest(long[] Ids);

/// <summary>Body of PUT /api/internal/privileges: the full set of privileged Discord user ids.</summary>
public sealed record PrivilegeSyncRequest(ulong[] UserIds);

/// <summary>Response of GET /api/internal/users/{discordId}: whether the user exists and, if so, its account hash.</summary>
public sealed record InternalUserDto(bool Exists, string? Hash);

/// <summary>Body of POST /api/internal/users: enable a Discord user.</summary>
public sealed record AddUserRequest(ulong DiscordId, string Username);

/// <summary>Body of POST /api/internal/watch and POST /api/internal/unwatch.</summary>
public sealed record WatchRequest(ulong DiscordId, string[] Urls, bool Privileged);

/// <summary>Response of the watch/unwatch endpoints, carrying TrackGames'/UntrackGames' user-facing message.</summary>
public sealed record WatchResponse(bool Success, string Response);

/// <summary>One watchlist entry in the response of GET /api/internal/watch/{discordId}.</summary>
public sealed record InternalGameDto(string Title, string Url);

/// <summary>Body of POST /api/internal/link/consume: consume a link code on behalf of a Discord user.</summary>
public sealed record LinkConsumeRequest(string Code, ulong DiscordId, string Username);

/// <summary>
///     Response of POST /api/internal/link/consume; reuses the in-process
///     <see cref="LinkOutcomeKind" /> so the bot can map it exactly like the old direct call.
/// </summary>
public sealed record LinkConsumeResponse(LinkOutcomeKind Kind, string Message);
