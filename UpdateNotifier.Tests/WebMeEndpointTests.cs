using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Extensions;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Tests;

/// <summary>
///     Hosts the cookie-authenticated web API in-process on ASP.NET Core's TestServer, pointed at the
///     collection's migrated SQLite database. Sessions are minted directly through WebAuthService (the
///     register/login endpoints are rate limited; the service is the same one the endpoints resolve) and
///     presented as the un_session cookie, exactly the shape the browser sends. Like
///     <see cref="InternalApiFixture" />, <see cref="Config" /> reads process-global environment
///     variables at construction, so SELF_HOSTED is nulled while the host is built and the singleton is
///     forced - the Database collection serializes tests, making the save/restore safe.
/// </summary>
public sealed class WebApiFixture : IDisposable
{
	public WebApiFixture()
	{
		var previousSelfHosted = Environment.GetEnvironmentVariable("SELF_HOSTED");
		// deterministic Config: SelfHosted off, so the only privilege path left is the synced table
		Environment.SetEnvironmentVariable("SELF_HOSTED", null);
		try
		{
			App = BuildApp();
			Config = App.Services.GetRequiredService<Config>(); // construct now, while SELF_HOSTED is null
			Server = App.GetTestServer();
			Client = new HttpClient(Server.CreateHandler()) { BaseAddress = Server.BaseAddress };
		}
		finally
		{
			Environment.SetEnvironmentVariable("SELF_HOSTED", previousSelfHosted);
		}
	}

	public WebApplication App { get; }
	public TestServer Server { get; }
	public HttpClient Client { get; }
	public Config Config { get; }

	/// <summary>
	///     The web host minus the background loops (the monitor would poll the real feed). A Config
	///     registered after AddUpdateNotifierCore wins last, letting a test swap in a self-hosted one.
	/// </summary>
	public static WebApplication BuildApp(Config? configOverride = null)
	{
		var builder = WebApplication.CreateBuilder();
		builder.Logging.ClearProviders();
		builder.Services.AddUpdateNotifierCore();
		builder.Services.RemoveAll<IHostedService>();
		// last-wins override, added after AddUpdateNotifierCore: any accidental scraping attempt
		// fails loudly instead of reaching the network - tests pre-seed games instead
		builder.Services.AddSingleton<IHttpClientFactory>(ThrowingHttpClientFactory.Instance);
		if (configOverride is not null)
			builder.Services.AddSingleton(configOverride);
		builder.WebHost.UseTestServer();

		var app = builder.Build();
		// the same order as the app's Program: the per-endpoint CORS/rate-limit metadata is read here
		app.UseRouting();
		app.UseCors();
		app.UseRateLimiter();
		app.MapUpdateNotifierEndpoints();
		app.Start();
		return app;
	}

	public void Dispose()
	{
		Client.Dispose();
		((IDisposable)App).Dispose(); // stops the host and disposes the TestServer registered as its IServer
	}
}

[Collection(DatabaseFixture.CollectionName)]
public sealed class WebMeEndpointTests(DatabaseFixture db, WebApiFixture api) : IClassFixture<WebApiFixture>
{
	// this class's own disjoint Discord snowflake range so tests can share one database
	private const ulong UserIdBase = 5_300_000_000;

	private const string Password = "password123";

	private async Task<AuthResult> RegisterAsync(string username)
	{
		await using var ctx = db.CreateContext();
		var result = await new WebAuthService(ctx, NullLogger<WebAuthService>.Instance).RegisterAsync(username, Password);
		Assert.True(result.Success);
		return result;
	}

	private static async Task SeedDiscordUserAsync(DatabaseFixture db, ulong accountId, ulong userId, string username)
	{
		await using var ctx = db.CreateContext();
		ctx.Users.Add(new User(userId) { AccountId = accountId, DiscordUsername = username });
		await ctx.SaveChangesAsync();
	}

	private static async Task<JsonNode?> GetMeAsync(HttpClient client, string? token)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
		if (token is not null)
			request.Headers.Add("Cookie", $"{WebEndpointExtensions.SessionCookieName}={token}");

		using var response = await client.SendAsync(request);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		return JsonNode.Parse(await response.Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task Me_MissingOrUnknownSession_IsUnauthorized()
	{
		using var noCookie = await api.Client.GetAsync("/api/v1/auth/me");
		Assert.Equal(HttpStatusCode.Unauthorized, noCookie.StatusCode);

		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
		request.Headers.Add("Cookie",
		                    $"{WebEndpointExtensions.SessionCookieName}={Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}");
		using var forged = await api.Client.SendAsync(request);
		Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
	}

	[Fact]
	public async Task Me_FreeAccount_ReportsFreeUserLimit()
	{
		var register = await RegisterAsync("me-free");

		var body = await GetMeAsync(api.Client, register.Token);

		Assert.Equal(register.AccountId, (ulong) body!["accountId"]!);
		Assert.Equal("me-free", body["username"]!.GetValue<string>());
		Assert.Matches("^[0-9A-F]{40}$", body["hash"]!.GetValue<string>());
		Assert.False(body["discordLinked"]!.GetValue<bool>());
		Assert.Null(body["discordUsername"]);
		Assert.Equal(Config.FREE_USER_LIMIT - 1, body["gameLimit"]!.GetValue<int>()); // enforcement rejects at >= FREE_USER_LIMIT, so 68 is the reachable max
	}

	[Fact]
	public async Task Me_LinkedButUnprivileged_StillLimited()
	{
		const ulong userId = UserIdBase + 1;
		var register = await RegisterAsync("me-linked-unpriv");
		await SeedDiscordUserAsync(db, register.AccountId, userId, "linked-unpriv"); // linked, no privilege row

		var body = await GetMeAsync(api.Client, register.Token);

		Assert.True(body!["discordLinked"]!.GetValue<bool>());
		Assert.Equal("linked-unpriv", body["discordUsername"]!.GetValue<string>());
		Assert.Equal(Config.FREE_USER_LIMIT - 1, body["gameLimit"]!.GetValue<int>()); // a link alone grants nothing
	}

	[Fact]
	public async Task Me_PrivilegedLinkedDiscordUser_ReportsUnlimited()
	{
		const ulong userId = UserIdBase + 11;
		var register = await RegisterAsync("me-priv");
		await SeedDiscordUserAsync(db, register.AccountId, userId, "priv-user");
		await using (var ctx = db.CreateContext())
		{
			ctx.PrivilegedUsers.Add(new PrivilegedUser(userId));
			await ctx.SaveChangesAsync();
		}

		var body = await GetMeAsync(api.Client, register.Token);

		Assert.True(body!["discordLinked"]!.GetValue<bool>());
		Assert.True(((JsonObject)body).ContainsKey("gameLimit")); // the field is present...
		Assert.Null(body["gameLimit"]); // ...and JSON null means unlimited
	}

	[Fact]
	public async Task Me_SelfHostedInstance_ReportsUnlimited()
	{
		// Config reads the environment at construction: build a self-hosted instance under a scoped
		// SELF_HOSTED=true, then let a last-wins registration replace the host's own Config
		var previous = Environment.GetEnvironmentVariable("SELF_HOSTED");
		Environment.SetEnvironmentVariable("SELF_HOSTED", "true");
		Config selfHosted;
		try
		{
			selfHosted = new Config(NullLogger<Config>.Instance);
		}
		finally
		{
			Environment.SetEnvironmentVariable("SELF_HOSTED", previous);
		}

		await using var app = WebApiFixture.BuildApp(selfHosted);
		var server = app.GetTestServer();
		using var client = new HttpClient(server.CreateHandler()) { BaseAddress = server.BaseAddress };
		var register = await RegisterAsync("me-selfhosted"); // no Discord link at all

		var body = await GetMeAsync(client, register.Token);

		Assert.False(body!["discordLinked"]!.GetValue<bool>()); // privileged by deployment, not by link
		Assert.True(((JsonObject)body).ContainsKey("gameLimit"));
		Assert.Null(body["gameLimit"]);
	}
}
