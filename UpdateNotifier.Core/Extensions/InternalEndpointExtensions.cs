using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Communication;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Extensions;

/// <summary>
///     Maps the container-to-container API the bot consumes: a public /health probe plus the
///     /api/internal group guarded by a shared API key and a CIDR allowlist. These endpoints carry
///     no rate-limit metadata on purpose and stay out of the OpenAPI document (the doc filter only
///     includes api/v1/* paths).
/// </summary>
public static class InternalEndpointExtensions
{
	/// <summary>Header carrying the shared bot &lt;-&gt; API key.</summary>
	internal const string InternalApiKeyHeader = "X-Internal-Api-Key";

	/// <summary>Past this many fetch attempts a notification is dead-lettered instead of re-queued.</summary>
	private const int MaxAttempts = 10;

	public static IEndpointRouteBuilder MapInternalEndpoints(this IEndpointRouteBuilder endpoints)
	{
		// deliberately unauthenticated: container healthchecks must not need the API key
		endpoints.MapGet("/health", () => Results.Ok(new { status = "ok" }));

		var group = endpoints.MapGroup("/api/internal")
		                     .AddEndpointFilter(new InternalAccessFilter());

		MapNotificationEndpoints(group);
		MapPrivilegeEndpoints(group);
		MapUserEndpoints(group);
		MapWatchEndpoints(group);
		MapLinkEndpoints(group);
		return endpoints;
	}

	private static void MapNotificationEndpoints(IEndpointRouteBuilder endpoints)
	{
		// claim-and-return: every fetched row's attempt counter is incremented (and persisted)
		// under the mutation lock before the rows are handed out, so a crash before the ack - or a
		// second poller - can only ever observe a row's Attempts, never re-deliver it for free
		endpoints.MapGet("/notifications/pending",
			async (DataContext db, ILogger<InternalEndpointsLog> logger, CancellationToken ct = default, [FromQuery] int limit = 50) =>
			{
				// a missing or non-positive limit falls back to the documented default
				if (limit <= 0) limit = 50;

				await DataContext.MutationLock.WaitAsync(ct);
				try
				{
					var batch = await db.PendingNotifications
					                    .Where(n => n.Status == PendingNotificationStatus.Pending)
					                    .OrderBy(n => n.Id)
					                    .Take(limit)
					                    .ToListAsync(ct);

					var deliverable = new List<PendingNotificationDto>(batch.Count);
					foreach (var notification in batch)
					{
						notification.Attempts++;
						if (notification.Attempts > MaxAttempts)
						{
							notification.Status = PendingNotificationStatus.Dead;
							logger.ZLogWarning($"Dead-lettering notification {notification.Id} for user {notification.DiscordUserId} after {notification.Attempts} attempts");
							continue;
						}

						deliverable.Add(new PendingNotificationDto(notification.Id, notification.DiscordUserId, notification.Message));
					}

					await db.SaveChangesAsync(ct);
					return Results.Json(deliverable);
				}
				finally
				{
					DataContext.MutationLock.Release();
				}
			});

		endpoints.MapPost("/notifications/ack",
			async ([FromBody] AckRequest request, DataContext db, CancellationToken ct) =>
			{
				await DataContext.MutationLock.WaitAsync(ct);
				try
				{
					// only Pending rows move: an ack must not resurrect an already dead-lettered row
					await db.PendingNotifications
					        .Where(n => request.Ids.Contains(n.Id) && n.Status == PendingNotificationStatus.Pending)
					        .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, PendingNotificationStatus.Sent), ct);
					return Results.NoContent();
				}
				finally
				{
					DataContext.MutationLock.Release();
				}
			});
	}

	private static void MapPrivilegeEndpoints(IEndpointRouteBuilder endpoints)
	{
		// full replace: the bot always pushes the complete privileged set; an empty set clears the cache
		endpoints.MapPut("/privileges",
			async ([FromBody] PrivilegeSyncRequest request, DataContext db, ILogger<InternalEndpointsLog> logger, CancellationToken ct) =>
			{
				await DataContext.MutationLock.WaitAsync(ct);
				try
				{
					// distinct: a duplicated snowflake would trip the PK and fail the whole sync
					var userIds = request.UserIds.Distinct().ToList();

					await using var transaction = await db.Database.BeginTransactionAsync(ct);
					await db.PrivilegedUsers.ExecuteDeleteAsync(ct);
					db.PrivilegedUsers.AddRange(userIds.Select(id => new PrivilegedUser(id)));
					await db.SaveChangesAsync(ct);
					await transaction.CommitAsync(ct);

					logger.ZLogInformation($"Synced {userIds.Count} privileged user(s)");
					return Results.NoContent();
				}
				finally
				{
					DataContext.MutationLock.Release();
				}
			});
	}

	private static void MapUserEndpoints(IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet("/users/{discordId:long}",
			async (ulong discordId, DataContext db, CancellationToken ct) =>
			{
				var account = (await db.Users.Include(u => u.Account)
				                          .FirstOrDefaultAsync(u => u.UserId == discordId, ct))?.Account;
				return Results.Ok(new InternalUserDto(account != null, account?.Hash));
			});

		// AddUser is the synchronous DataContext API the bot called directly - kept as-is
		endpoints.MapPost("/users",
			([FromBody] AddUserRequest request, DataContext db)
				=> Results.Ok(db.AddUser(request.DiscordId, request.Username)));

		endpoints.MapDelete("/users/{discordId:long}",
			async (ulong discordId, DataContext db) => Results.Ok(await db.RemoveUser(discordId)));
	}

	private static void MapWatchEndpoints(IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/watch",
			async ([FromBody] WatchRequest request, DataContext db, ILogger<InternalEndpointsLog> logger, CancellationToken ct) =>
			{
				if (await FindAccountAsync(db, request.DiscordId, ct) is not { } account)
					return UseEnableFirst();

				var (success, response) = await db.TrackGames(account.Hash, request.Urls, request.Privileged, ct);
				logger.ZLogDebug($"Watch for {request.DiscordId} [{success}]: {response}");
				// 200 even on success=false: the response string is the user-facing message
				// (e.g. the free-tier limit), not an API error
				return Results.Ok(new WatchResponse(success, response));
			});

		endpoints.MapPost("/unwatch",
			async ([FromBody] WatchRequest request, DataContext db, ILogger<InternalEndpointsLog> logger, CancellationToken ct) =>
			{
				if (await FindAccountAsync(db, request.DiscordId, ct) is not { } account)
					return UseEnableFirst();

				var (success, response) = await db.UntrackGames(account.Hash, request.Urls, request.Privileged, ct);
				logger.ZLogDebug($"Unwatch for {request.DiscordId} [{success}]: {response}");
				return Results.Ok(new WatchResponse(success, response));
			});

		endpoints.MapGet("/watch/{discordId:long}",
			async (ulong discordId, DataContext db, CancellationToken ct) =>
			{
				if (await FindAccountAsync(db, discordId, ct) is not { } account)
					return UseEnableFirst();

				// LastUpdated descending, like the bot's OrderByDescending(game => game):
				// Game's IComparable compares LastUpdated
				var games = await db.Watchlist.Where(w => w.AccountId == account.AccountId)
				                    .Select(w => w.Game!)
				                    .OrderByDescending(g => g.LastUpdated)
				                    .Select(g => new InternalGameDto(g.Title, g.Url))
				                    .ToListAsync(ct);
				return Results.Json(games);
			});
	}

	private static void MapLinkEndpoints(IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/link/consume",
			async ([FromBody] LinkConsumeRequest request, IWebAuthService auth, ILogger<InternalEndpointsLog> logger, CancellationToken ct) =>
			{
				try
				{
					var outcome = await auth.ConsumeLinkCodeAsync(request.Code, request.DiscordId, request.Username, ct);
					return Results.Ok(new LinkConsumeResponse(outcome.Kind, outcome.Message));
				}
				catch (Exception e)
				{
					// mirror the bot's LinkCommands: log the real error, answer with the generic
					// message it used to show
					logger.ZLogError(e, $"[{e.GetType()}]Failed to consume link code for user {request.DiscordId}");
					return Results.Json(new { error = "Something went wrong while linking. Please try again." },
					                    statusCode: StatusCodes.Status500InternalServerError);
				}
			});
	}

	/// <summary>The account linked to a Discord snowflake, or null when the user is not enabled.</summary>
	private static async Task<Account?> FindAccountAsync(DataContext db, ulong discordId, CancellationToken ct)
		=> (await db.Users.Include(u => u.Account)
		          .FirstOrDefaultAsync(u => u.UserId == discordId, ct))?.Account;

	/// <summary>The "no account" answer the bot maps to its old /enable-first replies.</summary>
	private static IResult UseEnableFirst()
		=> Results.Json(new { error = "User not found. Use /enable first." }, statusCode: StatusCodes.Status404NotFound);
}

/// <summary>Log category for the internal endpoints - a static class cannot be an ILogger type argument.</summary>
internal sealed class InternalEndpointsLog { }

/// <summary>
///     Guards the whole /api/internal group: a fixed-time API key comparison plus a CIDR allowlist
///     on the remote address. Stateless on purpose - everything it needs is resolved per request
///     from <see cref="HttpContext.RequestServices" />.
/// </summary>
internal sealed class InternalAccessFilter : IEndpointFilter
{
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		var http = context.HttpContext;
		var config = http.RequestServices.GetRequiredService<Config>();
		var logger = http.RequestServices.GetRequiredService<ILogger<InternalAccessFilter>>();

		if (!ApiKeyMatches(http.Request.Headers[InternalEndpointExtensions.InternalApiKeyHeader], config.InternalApiKey))
		{
			logger.ZLogWarning($"Rejected internal request from {http.Connection.RemoteIpAddress}: missing or invalid API key");
			return Results.Text("Invalid or missing internal API key.", statusCode: StatusCodes.Status401Unauthorized);
		}

		// a null remote address is never trusted - deny closed
		var remote = http.Connection.RemoteIpAddress;
		if (remote is null || !config.InternalApiAllowedCidrs.Any(network => network.Contains(remote)))
		{
			logger.ZLogWarning($"Rejected internal request from {remote?.ToString() ?? "unknown"}: address outside the CIDR allowlist");
			return Results.Text("Forbidden.", statusCode: StatusCodes.Status403Forbidden);
		}

		return await next(context);
	}

	/// <summary>
	///     Constant-time key comparison on the UTF-8 bytes. An empty configured key rejects everyone
	///     (the endpoints are not provisioned yet), and the length check runs first because
	///     FixedTimeEquals short-circuits on differing lengths.
	/// </summary>
	private static bool ApiKeyMatches(string? provided, string expected)
	{
		if (string.IsNullOrEmpty(expected)) return false;

		var providedBytes = Encoding.UTF8.GetBytes(provided ?? string.Empty);
		var expectedBytes = Encoding.UTF8.GetBytes(expected);
		return providedBytes.Length == expectedBytes.Length
		       && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
	}
}
