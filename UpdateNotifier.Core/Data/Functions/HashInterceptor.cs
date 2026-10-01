using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace UpdateNotifier.Data.Functions;

public class HashInterceptor : DbConnectionInterceptor
{
	public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
	{
		var sqliteConnection = (SqliteConnection)connection;
		sqliteConnection.CreateFunction<string, string>("user_hash", UserHash, true);
		base.ConnectionOpened(connection, eventData);
	}

	public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
	                                                 CancellationToken cancellationToken = default)
	{
		var sqliteConnection = (SqliteConnection)connection;
		sqliteConnection.CreateFunction<string, string>("user_hash", UserHash, true);
		await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
	}

	private string UserHash(string arg)
	{
		var hash = SHA1.HashData(Encoding.UTF8.GetBytes(arg));
		return Convert.ToHexString(hash);
	}
}