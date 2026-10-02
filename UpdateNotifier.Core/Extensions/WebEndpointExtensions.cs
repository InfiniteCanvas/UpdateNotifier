using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UpdateNotifier.Abstractions;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Data.Requests;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Extensions;

/// <summary>
///     Maps every UpdateNotifier HTTP endpoint: the session-cookie web auth surface, the /me profile
///     surface, and the extension's hash-authenticated game endpoints. CORS and rate-limit policy
///     names live here so they stay colocated with the endpoints that use them; the registrations
///     themselves are wired in <see cref="ServiceCollectionExtensions.AddUpdateNotifierCore" />.
/// </summary>
public static class WebEndpointExtensions
{
	internal const string CorsPolicyName = "AllowExtension";
	internal const string AuthRateLimitPolicy = "auth";
	internal const string MutationRateLimitPolicy = "mutations";

	internal const string SessionCookieName = "un_session";

	public static IEndpointRouteBuilder MapUpdateNotifierEndpoints(this IEndpointRouteBuilder endpoints)
	{
		MapAuthEndpoints(endpoints);
		MapMeEndpoints(endpoints);
		MapGameEndpoints(endpoints);
		return endpoints;
	}

	private static void MapAuthEndpoints(IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/api/v1/auth/register",
		        async (HttpContext http, [FromBody] AuthCredentialsRequest request, IWebAuthService auth, CancellationToken ct) =>
		        {
			        var result = await auth.RegisterAsync(request.Username, request.Password, ct);
			        if (!result.Success)
				        return result.Error == AuthError.UsernameTaken
					               ? Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status409Conflict)
					               : Results.BadRequest(new { error = result.Message });

			        SetSessionCookie(http.Response, result.Token, result.ExpiresAt, CookieSecure(http));
			        return Results.Json(new { result.AccountId, result.Username }, statusCode: StatusCodes.Status201Created);
		        })
		    .RequireRateLimiting(AuthRateLimitPolicy)
		    .WithName("Register")
		    .WithTags("Auth")
		    .WithSummary("Create a web account")
		    .WithDescription("Registers a username/password login and returns a session cookie");

		endpoints.MapPost("/api/v1/auth/login",
		        async (HttpContext http, [FromBody] AuthCredentialsRequest request, IWebAuthService auth, CancellationToken ct) =>
		        {
			        var result = await auth.LoginAsync(request.Username, request.Password, ct);
			        if (!result.Success)
				        return Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status401Unauthorized);

			        SetSessionCookie(http.Response, result.Token, result.ExpiresAt, CookieSecure(http));
			        return Results.Ok(new { result.AccountId, result.Username });
		        })
		    .RequireRateLimiting(AuthRateLimitPolicy)
		    .WithName("Login")
		    .WithTags("Auth")
		    .WithSummary("Sign in")
		    .WithDescription("Verifies credentials and returns a session cookie");

		endpoints.MapPost("/api/v1/auth/logout",
		        async (HttpContext http, IWebAuthService auth, CancellationToken ct) =>
		        {
			        // no session is not an error - the goal (no session) already holds
			        var token = http.Request.Cookies[SessionCookieName];
			        if (!string.IsNullOrEmpty(token))
				        await auth.LogoutAsync(token, ct);
			        ClearSessionCookie(http.Response);
			        return Results.NoContent();
		        })
		    .WithName("Logout")
		    .WithTags("Auth")
		    .WithSummary("End the session")
		    .WithDescription("Deletes the session row and expires the cookie");

		endpoints.MapPost("/api/v1/auth/delete",
		        async (HttpContext http, [FromBody] PasswordRequest request, IWebAuthService auth, CancellationToken ct) =>
		        {
			        var account = await GetAccountFromCookieAsync(http, auth, ct);
			        if (account is not { Account: { } target })
				        return NotSignedIn();

			        if (!await auth.DeleteAccountAsync(target.AccountId, request.Password, ct))
				        return Results.Json(new { error = "Invalid password." }, statusCode: StatusCodes.Status401Unauthorized);

			        ClearSessionCookie(http.Response);
			        return Results.NoContent();
		        })
		    .RequireRateLimiting(AuthRateLimitPolicy)
		    .WithName("DeleteAccount")
		    .WithTags("Auth")
		    .WithSummary("Delete the web account")
		    .WithDescription("Removes the account, its watchlist and every linked row; requires the password");

		endpoints.MapGet("/api/v1/auth/me",
		        async (HttpContext http, IWebAuthService auth, IPrivilegeChecker privileges, Config config, CancellationToken ct) =>
		        {
			        var account = await GetAccountFromCookieAsync(http, auth, ct);
			        if (account is not { Account: { } target })
				        return NotSignedIn();

			        // mirrors the limit enforcement in DataContext.TrackGames: self-hosted or
			        // bot-synced privilege lifts the cap. Enforcement rejects adds once
			        // count + incoming >= FREE_USER_LIMIT, so the largest watchlist a free
			        // account can actually reach is FREE_USER_LIMIT - 1 — report that.
			        var privileged = config.SelfHosted
			                         || (target.User is { } user && await privileges.IsPrivilegedAsync(user.UserId, ct));

			        return Results.Ok(new
			        {
				        target.AccountId,
				        Username = account.Username,
				        target.Hash,
				        DiscordLinked = target.User != null,
				        DiscordUsername = target.User?.DiscordUsername,
				        GameLimit = privileged ? (int?) null : Config.FREE_USER_LIMIT - 1
			        });
		        })
	        .WithName("GetMe")
	        .WithTags("Auth")
	        .WithSummary("Get the signed-in account")
	        .WithDescription("Returns account id, username, extension hash, Discord link status and the account's game limit (null = unlimited)");
	}

	private static void MapMeEndpoints(IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/api/v1/me/hash/regenerate",
		        async (HttpContext http, IWebAuthService auth, CancellationToken ct) =>
		        {
			        var account = await GetAccountFromCookieAsync(http, auth, ct);
			        if (account is not { Account: { } target })
				        return NotSignedIn();

			        var hash = await auth.RegenerateHashAsync(target.AccountId, ct);
			        return Results.Ok(new { Hash = hash });
		        })
		    .WithName("RegenerateHash")
		    .WithTags("Me")
		    .WithSummary("Rotate the extension hash")
		    .WithDescription("Replaces the watchlist hash used by the extension; the old one stops working immediately");

		endpoints.MapGet("/api/v1/me/games",
		        async (HttpContext http, DataContext db, IWebAuthService auth, CancellationToken ct) =>
		        {
			        var account = await GetAccountFromCookieAsync(http, auth, ct);
			        if (account is not { Account: { } target })
				        return NotSignedIn();

			        var games = await GetWatchedGamesAsync(db, target.AccountId, ct);
			        return Results.Ok(games);
		        })
		    .WithName("GetMyGames")
		    .WithTags("Me")
		    .WithSummary("List the watchlist")
		    .WithDescription("Returns the signed-in account's watched games, newest update first");

		endpoints.MapPost("/api/v1/me/link/code",
		        async (HttpContext http, IWebAuthService auth, CancellationToken ct) =>
		        {
			        var account = await GetAccountFromCookieAsync(http, auth, ct);
			        if (account is not { Account: { } target })
				        return NotSignedIn();

			        var link = await auth.CreateLinkCodeAsync(target.AccountId, ct);
			        if (link is null)
				        return Results.Json(new { error = "This account already has a Discord user linked." },
				                            statusCode: StatusCodes.Status409Conflict);

			        var (code, expiresAt) = link.Value;
			        return Results.Ok(new { Code = code, ExpiresAt = expiresAt });
		        })
		    .WithName("CreateLinkCode")
		    .WithTags("Me")
		    .WithSummary("Issue a Discord link code")
		    .WithDescription("Returns a short-lived code the Discord /link command consumes; 409 when already linked");

		endpoints.MapDelete("/api/v1/me/link",
		        async (HttpContext http, IWebAuthService auth, CancellationToken ct) =>
		        {
			        var account = await GetAccountFromCookieAsync(http, auth, ct);
			        if (account is not { Account: { } target })
				        return NotSignedIn();

			        if (!await auth.UnlinkDiscordAsync(target.AccountId, ct))
				        return Results.Json(new { error = "No Discord account is linked." }, statusCode: StatusCodes.Status409Conflict);

			        return Results.NoContent();
		        })
		    .WithName("UnlinkDiscord")
		    .WithTags("Me")
		    .WithSummary("Unlink the Discord account")
		    .WithDescription("Deletes only the Discord link; the web account, login and watchlist survive");
	}

	private static void MapGameEndpoints(IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPost("/api/v1/games",
		        async ([FromBody] GameAddRequest addRequest, IEndpointHandlerService handlerService,
		                CancellationToken ct)
		            => await handlerService.AddGameAsync(addRequest, ct))
		    .RequireCors(CorsPolicyName)
		    .RequireRateLimiting(MutationRateLimitPolicy)
		    .WithName("AddGame")
		    .WithTags("Game")
		    .WithSummary("Add game for tracking")
		    .WithDescription("Create a new watchlist entry for game tracking");

		endpoints.MapDelete("/api/v1/games",
		        async ([FromBody] GameAddRequest addRequest, IEndpointHandlerService handlerService,
		                CancellationToken ct)
		            => await handlerService.RemoveGameAsync(addRequest, ct))
		    .RequireCors(CorsPolicyName)
		    .RequireRateLimiting(MutationRateLimitPolicy)
		    .WithName("RemoveGame")
		    .WithTags("Game")
		    .WithSummary("Remove game from tracking")
		    .WithDescription("Remove a watchlist entry from game tracking");

		endpoints.MapGet("/api/v1/games",
		        async ([FromQuery] string userHash, IEndpointHandlerService handlerService, CancellationToken ct)
		            => await handlerService.GetWatchedGamesAsync(userHash, ct))
		    .RequireCors(CorsPolicyName)
		    .WithName("GetIsWatched")
		    .WithTags("Game")
		    .WithSummary("Get game tracking status")
		    .WithDescription("Get game tracking status");
	}

	internal static void SetSessionCookie(HttpResponse response, string token, DateTime expires, bool secure)
		=> response.Cookies.Append(SessionCookieName, token, new CookieOptions
		{
			HttpOnly = true,
			SameSite = SameSiteMode.Lax,
			Secure = secure,
			Path = "/",
			Expires = expires
		});

	internal static void ClearSessionCookie(HttpResponse response)
		=> response.Cookies.Append(SessionCookieName, string.Empty, new CookieOptions
		{
			HttpOnly = true,
			SameSite = SameSiteMode.Lax,
			Path = "/",
			Expires = DateTimeOffset.UnixEpoch // expire immediately
		});

	private static async Task<WebAccount?> GetAccountFromCookieAsync(HttpContext http, IWebAuthService auth, CancellationToken ct)
	{
		var token = http.Request.Cookies[SessionCookieName];
		if (string.IsNullOrEmpty(token))
			return null;

		var session = await auth.ValidateSessionAsync(token, ct);
		if (session is null)
			return null;

		return await auth.GetAccountAsync(session.AccountId, ct);
	}

	internal static Task<List<WatchedGameDto>> GetWatchedGamesAsync(DataContext db, ulong accountId, CancellationToken ct = default)
		=> db.Watchlist.Where(w => w.AccountId == accountId)
		      .Select(w => w.Game!)
		      .OrderByDescending(g => g.LastUpdated)
		      .Select(g => new WatchedGameDto(g.GameId, g.Title, g.Url, g.LastUpdated, g.ThumbnailUrl))
		      .ToListAsync(ct);

	internal sealed record WatchedGameDto(ulong GameId, string Title, string Url, DateTime LastUpdated, string? ThumbnailUrl);

	private static bool CookieSecure(HttpContext http)
		=> http.RequestServices.GetRequiredService<Config>().CookieSecure;

	private static IResult NotSignedIn()
		=> Results.Json(new { error = "Not signed in." }, statusCode: StatusCodes.Status401Unauthorized);
}
