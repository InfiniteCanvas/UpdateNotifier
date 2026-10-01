using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Data;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Tests;

/// <summary>
///     The database location is reachable only through the DATABASE_PATH environment variable
///     (DataContext.OnConfiguring), so the fixture owns that variable and points it at a temp file.
///     Everything that touches the database lives in this collection: xunit runs collections in
///     parallel, and environment variables are process-global.
/// </summary>
public sealed class DatabaseFixture : IDisposable
{
	public const string CollectionName = "Database";

	public DatabaseFixture()
	{
		DatabasePath = Path.Combine(Path.GetTempPath(), $"update-notifier-tests-{Guid.NewGuid():N}.db");
		Environment.SetEnvironmentVariable("DATABASE_PATH", DatabasePath);

		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<IHttpClientFactory>(ThrowingHttpClientFactory.Instance);
		services.AddSingleton<Config>();
		services.AddSingleton<GameInfoProvider>();
		Services = services.BuildServiceProvider();

		// User.Hash is a stored computed column calling user_hash(), which the context's
		// HashInterceptor registers on every connection open - so the schema must be created
		// through a real DataContext, not by hand.
		using (var db = CreateContext())
			db.Database.Migrate();
	}

	public string DatabasePath { get; }

	public IServiceProvider Services { get; }

	public DataContext CreateContext()
		=> new(Services.GetRequiredService<ILogger<DataContext>>(),
		       Services.GetRequiredService<Config>(),
		       Services.GetRequiredService<GameInfoProvider>());

	public void Dispose()
	{
		(Services as IDisposable)?.Dispose();
		// Microsoft.Data.Sqlite pools connections; the file stays locked until the pools are cleared.
		SqliteConnection.ClearAllPools();
		TryDelete(DatabasePath);
		TryDelete(DatabasePath + "-journal");
		TryDelete(DatabasePath + "-wal");
		TryDelete(DatabasePath + "-shm");
		return;

		static void TryDelete(string path)
		{
			try
			{
				File.Delete(path);
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
		}
	}
}

[CollectionDefinition(DatabaseFixture.CollectionName)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }
