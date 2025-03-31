using Discord;
using Discord.WebSocket;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

public class EndpointHandlerService(DataContext db, ILogger<EndpointHandlerService> logger, PrivilegeCheckerService privilegeCheckerService, DiscordSocketClient client)
	: IEndpointHandlerService
{
	public async ValueTask<IResult> AddGameAsync(GameAddRequest request, CancellationToken ct = default)
	{
		logger.ZLogDebug($"Request: {request}");
		var user = db.Users.FirstOrDefault(user => user.Hash == request.UserHash);
		if (user == null)
		{
			logger.ZLogError($"User {request.UserHash} was not found");
			return Results.BadRequest("User was not found");
		}

		var (success, response) = await db.AddGames(user.UserId, await privilegeCheckerService.IsPrivileged(user.UserId), [request.ThreadUrl], ct);

		if (!request.DiscordNotification) return success ? Results.Ok(response) : Results.BadRequest(response);

		logger.ZLogDebug($"Success [{success}]: {response}");
		var discordUser = await client.GetUserAsync(user.UserId);
		await discordUser.SendMessageAsync(response);

		return success ? Results.Ok(response) : Results.BadRequest(response);
	}

	public async ValueTask<IResult> RemoveGameAsync(GameAddRequest request, CancellationToken ct = default)
	{
		logger.ZLogDebug($"Request: {request}");
		var user = db.Users.FirstOrDefault(user => user.Hash == request.UserHash);
		if (user == null)
		{
			logger.ZLogError($"User {request.UserHash} was not found");
			return Results.BadRequest("User was not found");
		}

		var (success, response) = await db.RemoveGames(user.UserId, await privilegeCheckerService.IsPrivileged(user.UserId), [request.ThreadUrl], ct);

		if (!request.DiscordNotification) return success ? Results.Ok(response) : Results.BadRequest(response);

		logger.ZLogDebug($"Success [{success}]: {response}");
		var discordUser = await client.GetUserAsync(user.UserId);
		await discordUser.SendMessageAsync(response);

		return success ? Results.Ok(response) : Results.BadRequest(response);
	}

	public ValueTask<IResult> GetWatchedGamesAsync(string userHash, CancellationToken ct = default)
	{
		logger.ZLogDebug($"Retrieving watched games for {userHash}");
		var user = db.Users.Include(u => u.Games).FirstOrDefault(user => userHash == user.Hash);
		if (user != null) return ValueTask.FromResult(Results.Ok(user.Games.Select(g => g.GameId)));

		logger.ZLogError($"User {userHash} was not found");
		return ValueTask.FromResult(Results.BadRequest("User was not found"));
	}
}