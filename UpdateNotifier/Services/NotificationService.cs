using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data;
using UpdateNotifier.Data.Models;
using ZLogger;

namespace UpdateNotifier.Services;

public sealed class NotificationService(DataContext db, ILogger<NotificationService> logger)
{
	public async Task<bool> UserExistsAsync(ulong userId) => await db.Users.AnyAsync(u => u.UserId == userId);

	public async Task<bool> AddUserAsync(ulong userId)
	{
		try
		{
			if (await UserExistsAsync(userId))
			{
				logger.ZLogTrace($"User {userId} already exists in database");
				return true;
			}

			var user = new DiscordUser { UserId = userId };

			await db.Users.AddAsync(user);
			await db.SaveChangesAsync();

			logger.ZLogInformation($"User {userId} has been added to database");
			return true;
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"Error adding user {userId} to database");
			return false;
		}
	}

	public async Task<bool> RemoveUserAsync(ulong userId)
	{
		try
		{
			var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
			if (user == null)
			{
				logger.ZLogTrace($"User {userId} does not exist in database");
				// return true here since user deletion was the goal and user does not exist
				return true;
			}

			db.Users.Remove(user);
			await db.SaveChangesAsync();
			logger.ZLogInformation($"User {userId} has been removed from database");
			return true;
		}
		catch (Exception e)
		{
			logger.ZLogError(e, $"Error removing user {userId} from database");
			return false;
		}
	}
}