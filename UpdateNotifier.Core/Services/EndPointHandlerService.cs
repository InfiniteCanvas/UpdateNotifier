using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Abstractions;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Requests;
using UpdateNotifier.Utilities;
using ZLogger;

namespace UpdateNotifier.Services;

public interface IEndpointHandlerService
{
	public ValueTask<IResult> AddGameAsync(GameAddRequest request, CancellationToken ct = default);

	public ValueTask<IResult> RemoveGameAsync(GameAddRequest request, CancellationToken ct = default);

	public ValueTask<IResult> GetWatchedGamesAsync(string userHash, CancellationToken ct = default);
}

public class EndpointHandlerService(DataContext db, ILogger<EndpointHandlerService> logger, IPrivilegeChecker privilegeChecker, IDmSender dmSender)
	: IEndpointHandlerService
{
	public async ValueTask<IResult> AddGameAsync(GameAddRequest request, CancellationToken ct = default)
	{
		logger.ZLogDebug($"Request: {request}");
		var account = await db.GetAccountByHashAsync(request.UserHash, ct);
		if (account == null)
		{
			logger.ZLogError($"User {request.UserHash.RedactHash()} was not found");
			return Results.NotFound("User was not found");
		}

		// privilege rides on the linked Discord identity; web-only accounts are never privileged
		var privileged = account.User == null ? false : await privilegeChecker.IsPrivilegedAsync(account.User.UserId, ct);
		var (success, response) = await db.TrackGames(request.UserHash, [request.ThreadUrl], privileged, ct);

		if (!request.DiscordNotification) return success ? Results.Ok(response) : Results.BadRequest(response);

		logger.ZLogDebug($"Success [{success}]: {response}");
		if (account.User != null)
			await dmSender.SendDmAsync(account.User.UserId, response, ct);

		return success ? Results.Ok(response) : Results.BadRequest(response);
	}

	public async ValueTask<IResult> RemoveGameAsync(GameAddRequest request, CancellationToken ct = default)
	{
		logger.ZLogDebug($"Request: {request}");
		var account = await db.GetAccountByHashAsync(request.UserHash, ct);
		if (account == null)
		{
			logger.ZLogError($"User {request.UserHash.RedactHash()} was not found");
			return Results.NotFound("User was not found");
		}

		var privileged = account.User == null ? false : await privilegeChecker.IsPrivilegedAsync(account.User.UserId, ct);
		var (success, response) = await db.UntrackGames(request.UserHash, [request.ThreadUrl], privileged, ct);

		if (!request.DiscordNotification) return success ? Results.Ok(response) : Results.BadRequest(response);

		logger.ZLogDebug($"Success [{success}]: {response}");
		if (account.User != null)
			await dmSender.SendDmAsync(account.User.UserId, response, ct);

		return success ? Results.Ok(response) : Results.BadRequest(response);
	}

	public async ValueTask<IResult> GetWatchedGamesAsync(string userHash, CancellationToken ct = default)
	{
		logger.ZLogDebug($"Retrieving watched games for {userHash.RedactHash()}");
		var account = await db.Accounts.Include(a => a.Games)
		                      .FirstOrDefaultAsync(a => a.Hash == userHash.Trim().ToUpperInvariant(), ct);
		if (account != null) return Results.Ok(account.Games.Select(g => g.GameId));

		logger.ZLogError($"User {userHash.RedactHash()} was not found");
		return Results.NotFound("User was not found");
	}
}
