using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UpdateNotifier.Abstractions;
using UpdateNotifier.Communication;
using UpdateNotifier.Data.Models;
using UpdateNotifier.Extensions;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Tests;

/// <summary>
///     Hosts the container-split internal API in-process on ASP.NET Core's TestServer, pointed at
///     the collection's migrated SQLite database. TestServer requests originate from loopback
///     (inside the default CIDR allowlist) and the fixture's client carries the shared API key -
///     exactly the shape of the bot container's calls. A class fixture inside the Database
///     collection because <see cref="Config" /> reads process-global environment variables at
///     construction, the same discipline <see cref="DatabaseFixture" /> applies to DATABASE_PATH:
///     the relevant variables are set, the host is built, Config is forced to construct, and the
///     previous values are restored before any test runs.
/// </summary>
public sealed class InternalApiFixture : IDisposable
{
    public const string ApiKey = "test-internal-api-key";

    public InternalApiFixture()
    {
        var previousKey = Environment.GetEnvironmentVariable("INTERNAL_API_KEY");
        var previousCidrs = Environment.GetEnvironmentVariable("INTERNAL_API_ALLOWED_CIDRS");
        var previousSelfHosted = Environment.GetEnvironmentVariable("SELF_HOSTED");
        // deterministic Config: a known key, the default CIDR allowlist, SelfHosted off
        Environment.SetEnvironmentVariable("INTERNAL_API_KEY", ApiKey);
        Environment.SetEnvironmentVariable("INTERNAL_API_ALLOWED_CIDRS", null);
        Environment.SetEnvironmentVariable("SELF_HOSTED", null);
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddUpdateNotifierCore();
            // the RSS/notification loops must never start: the monitor would poll the real feed
            builder.Services.RemoveAll<IHostedService>();
            // last-wins override, added after AddUpdateNotifierCore: any accidental scraping
            // attempt fails loudly instead of reaching the network - tests pre-seed games instead
            builder.Services.AddSingleton<IHttpClientFactory>(ThrowingHttpClientFactory.Instance);
            builder.WebHost.UseTestServer();

            App = builder.Build();
            App.MapInternalEndpoints();
            App.Start();

            // Config reads the environment at construction: force the singleton now, while the
            // test key is still set - a lazy construction would read the restored environment
            Config = App.Services.GetRequiredService<Config>();

            Server = App.GetTestServer();
            // TestServer leaves Connection.RemoteIpAddress null by default and the internal API
            // denies closed - so emulate the bot container's loopback origin on every request via
            // CreateHandler's server-side callback (a plain CreateClient stays keyless/null-origin
            // for the auth tests).
            Client = new HttpClient(Server.CreateHandler(
                http => http.Connection.RemoteIpAddress = IPAddress.Loopback))
            {
                BaseAddress = Server.BaseAddress
            };
            Client.DefaultRequestHeaders.Add(InternalEndpointExtensions.InternalApiKeyHeader, ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("INTERNAL_API_KEY", previousKey);
            Environment.SetEnvironmentVariable("INTERNAL_API_ALLOWED_CIDRS", previousCidrs);
            Environment.SetEnvironmentVariable("SELF_HOSTED", previousSelfHosted);
        }
    }

    public WebApplication App { get; }
    public TestServer Server { get; }
    public HttpClient Client { get; }
    public Config Config { get; }

    public void Dispose()
    {
        Client.Dispose();
        ((IDisposable)App).Dispose(); // stops the host and disposes the TestServer registered as its IServer
    }
}

[Collection(DatabaseFixture.CollectionName)]
public sealed class InternalApiTests(DatabaseFixture db, InternalApiFixture api) : IClassFixture<InternalApiFixture>
{
    // Each test class uses its own disjoint id range so tests can share one database.
    private const ulong GameIdBase = 5_000_000_000;
    private const ulong UserIdBase = 5_100_000_000;

    private static string ThreadUrl(ulong gameId)
        => $"https://f95zone.to/threads/some-game.{gameId}/"; // sanitizes to /threads/{gameId}

    private static async Task SeedGameAsync(DatabaseFixture db, ulong gameId, string title, DateTime lastUpdated)
    {
        await using var seed = db.CreateContext();
        seed.Games.Add(new Game(gameId, title, lastUpdated, $"https://f95zone.to/threads/{gameId}"));
        await seed.SaveChangesAsync();
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        return body!["error"]!.GetValue<string>();
    }

    // ---- auth ---------------------------------------------------------------------------------

    [Fact]
    public async Task Health_IsPublic_NoKeyRequired()
    {
        using var client = api.Server.CreateClient(); // no X-Internal-Api-Key header at all

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", body!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task InternalGroup_MissingApiKey_IsUnauthorized()
    {
        using var client = api.Server.CreateClient();

        using var response = await client.GetAsync("/api/internal/notifications/pending");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InternalGroup_WrongApiKey_IsUnauthorized()
    {
        using var client = api.Server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/internal/notifications/pending");
        request.Headers.Add(InternalEndpointExtensions.InternalApiKeyHeader, "not-the-key");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InternalGroup_CorrectKeyFromLoopback_IsAllowed()
    {
        // the fixture's client sends with the shared key from a loopback origin (set per request
        // by CreateHandler), which the default CIDR allowlist trusts

        using var response = await api.Client.GetAsync("/api/internal/notifications/pending");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<List<PendingNotificationDto>>());
    }

    // ---- InternalAccessFilter, driven directly (the 403 path needs a spoofed remote address) --

    private static async Task<object?> InvokeFilterAsync(DefaultHttpContext http, object? sentinel = null)
    {
        var filter = new InternalAccessFilter();
        return await filter.InvokeAsync(EndpointFilterInvocationContext.Create(http),
                                        _ => ValueTask.FromResult(sentinel));
    }

    private DefaultHttpContext MakeHttpContext(string? remoteIp)
    {
        var services = new ServiceCollection();
        services.AddSingleton(api.Config); // constructed with the test key by the fixture
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<InternalAccessFilter>>(
            NullLogger<InternalAccessFilter>.Instance);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Request.Headers[InternalEndpointExtensions.InternalApiKeyHeader] = InternalApiFixture.ApiKey;
        if (remoteIp != null) http.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        return http;
    }

    [Fact]
    public async Task InternalAccessFilter_PublicRemoteAddress_IsForbidden()
    {
        var http = MakeHttpContext("8.8.8.8"); // correct key, but outside every allowed network

        var result = await InvokeFilterAsync(http);

        Assert.Equal(StatusCodes.Status403Forbidden, ((IStatusCodeHttpResult)result!).StatusCode);
    }

    [Fact]
    public async Task InternalAccessFilter_MissingRemoteAddress_IsForbidden()
    {
        var http = MakeHttpContext(null); // never trust a null remote address

        var result = await InvokeFilterAsync(http);

        Assert.Equal(StatusCodes.Status403Forbidden, ((IStatusCodeHttpResult)result!).StatusCode);
    }

    [Fact]
    public async Task InternalAccessFilter_LoopbackWithKey_ReachesHandler()
    {
        var sentinel = Results.Ok();
        var http = MakeHttpContext("127.0.0.1");

        var result = await InvokeFilterAsync(http, sentinel);

        Assert.Same(sentinel, result); // the filter deferred to the endpoint
    }

    // ---- notification queue -------------------------------------------------------------------

    [Fact]
    public async Task QueuedNotifications_RoundTrip_PendingOldestFirstThenAcked()
    {
        const ulong user = UserIdBase + 1;
        var sender = api.App.Services.GetRequiredService<IDmSender>();
        Assert.IsType<QueuedDmSender>(sender);

        await sender.SendDmAsync(user, "first message");
        await sender.SendDmAsync(user, "second message");

        using var pending = await api.Client.GetAsync("/api/internal/notifications/pending");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        var batch = (await pending.Content.ReadFromJsonAsync<List<PendingNotificationDto>>())!
            .Where(dto => dto.DiscordUserId == user).ToList();
        Assert.Equal(2, batch.Count);
        // oldest-first: the first-enqueued row carries the lower autoincrement id
        Assert.True(batch[0].Id < batch[1].Id);
        Assert.Equal("first message", batch[0].Message);
        Assert.Equal("second message", batch[1].Message);

        using (var ack = await api.Client.PostAsJsonAsync("/api/internal/notifications/ack",
                new AckRequest(batch.Select(dto => dto.Id).ToArray())))
        {
            Assert.Equal(HttpStatusCode.NoContent, ack.StatusCode);
        }

        using (var after = await api.Client.GetAsync("/api/internal/notifications/pending"))
        {
            var remaining = (await after.Content.ReadFromJsonAsync<List<PendingNotificationDto>>())!;
            Assert.DoesNotContain(remaining, dto => dto.DiscordUserId == user);
        }

        await using var verify = db.CreateContext();
        var statuses = await verify.PendingNotifications.Where(n => n.DiscordUserId == user)
                                   .Select(n => n.Status).ToListAsync();
        Assert.Equal([PendingNotificationStatus.Sent, PendingNotificationStatus.Sent], statuses);
    }

    [Fact]
    public async Task ExhaustedNotification_IsDeadLettered_AndCannotBeResurrectedByAck()
    {
        const ulong user = UserIdBase + 11;
        long deadId;
        await using (var seed = db.CreateContext())
        {
            var dead = new PendingNotification(user, "poison") { Attempts = 10, CreatedAt = DateTime.UtcNow };
            seed.PendingNotifications.AddRange(dead,
                                               new PendingNotification(user, "fine") { CreatedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
            deadId = dead.Id;
        }

        using var pending = await api.Client.GetAsync("/api/internal/notifications/pending");
        var batch = (await pending.Content.ReadFromJsonAsync<List<PendingNotificationDto>>())!;
        // the claim increments the exhausted row past the cap and dead-letters it instead of handing it out
        Assert.DoesNotContain(batch, dto => dto.Id == deadId);
        Assert.Contains(batch, dto => dto.DiscordUserId == user && dto.Message == "fine");

        await using (var verify = db.CreateContext())
        {
            var deadRow = await verify.PendingNotifications.SingleAsync(n => n.Id == deadId);
            Assert.Equal(PendingNotificationStatus.Dead, deadRow.Status);
            Assert.Equal(11, deadRow.Attempts);
        }

        using (var ack = await api.Client.PostAsJsonAsync("/api/internal/notifications/ack", new AckRequest([deadId])))
        {
            Assert.Equal(HttpStatusCode.NoContent, ack.StatusCode);
        }

        await using var recheck = db.CreateContext();
        Assert.Equal(PendingNotificationStatus.Dead, // acks only move Pending -> Sent
            (await recheck.PendingNotifications.SingleAsync(n => n.Id == deadId)).Status);
    }

    // ---- privilege sync -----------------------------------------------------------------------

    [Fact]
    public async Task PrivilegeSync_ReplacesTheWholeSet_ForSyncedChecker()
    {
        const ulong member = UserIdBase + 21;
        const ulong other = UserIdBase + 22;
        const ulong outsider = UserIdBase + 23;
        var checker = api.App.Services.GetRequiredService<IPrivilegeChecker>();
        Assert.IsType<SyncedPrivilegeChecker>(checker);

        using (var put = await api.Client.PutAsJsonAsync("/api/internal/privileges",
                // a duplicated snowflake must not fail the sync (the endpoint deduplicates)
                new PrivilegeSyncRequest([member, member, other])))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        Assert.True(await checker.IsPrivilegedAsync(member));
        Assert.True(await checker.IsPrivilegedAsync(other));
        Assert.False(await checker.IsPrivilegedAsync(outsider));

        using (var clear = await api.Client.PutAsJsonAsync("/api/internal/privileges",
                new PrivilegeSyncRequest([])))
        {
            Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);
        }

        Assert.False(await checker.IsPrivilegedAsync(member)); // replace-all: the empty set clears the cache
    }

    // ---- user lifecycle -----------------------------------------------------------------------

    [Fact]
    public async Task Users_AddThenLookupThenDelete_Lifecycle()
    {
        const ulong user = UserIdBase + 31;

        using (var add = await api.Client.PostAsJsonAsync("/api/internal/users",
                                                          new AddUserRequest(user, "lifecycle-user")))
        {
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
            Assert.True(await add.Content.ReadFromJsonAsync<bool>());
        }

        using (var get = await api.Client.GetAsync($"/api/internal/users/{user}"))
        {
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var dto = await get.Content.ReadFromJsonAsync<InternalUserDto>();
            Assert.NotNull(dto);
            Assert.True(dto!.Exists);
            Assert.Matches("^[0-9A-F]{40}$", dto.Hash); // the account hash, the shape AddUser generates
        }

        using (var delete = await api.Client.DeleteAsync($"/api/internal/users/{user}"))
        {
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
            Assert.True(await delete.Content.ReadFromJsonAsync<bool>());
        }

        using (var gone = await api.Client.GetAsync($"/api/internal/users/{user}"))
        {
            Assert.Equal(HttpStatusCode.OK, gone.StatusCode);
            var dto = await gone.Content.ReadFromJsonAsync<InternalUserDto>();
            Assert.NotNull(dto);
            Assert.False(dto!.Exists);
            Assert.Null(dto.Hash);
        }
    }

    // ---- watch --------------------------------------------------------------------------------

    [Fact]
    public async Task Watch_UnknownUser_AnswersNotFound()
    {
        const ulong unknown = UserIdBase + 41;

        using (var watch = await api.Client.PostAsJsonAsync("/api/internal/watch",
                new WatchRequest(unknown, [ThreadUrl(GameIdBase + 41)], false)))
        {
            Assert.Equal(HttpStatusCode.NotFound, watch.StatusCode);
            Assert.Equal("User not found.", await ErrorAsync(watch));
        }

        using (var list = await api.Client.GetAsync($"/api/internal/watch/{unknown}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
            Assert.Equal("User not found.", await ErrorAsync(list));
        }
    }

    [Fact]
    public async Task Watch_AddListsByLastUpdatedDescThenRemoves_WithoutScraping()
    {
        const ulong user = UserIdBase + 51;
        const ulong older = GameIdBase + 1;
        const ulong newer = GameIdBase + 2;

        // enable through the API itself; pre-seed both games so TrackGames never scrapes
        using (var enable = await api.Client.PostAsJsonAsync("/api/internal/users",
                                                             new AddUserRequest(user, "watcher")))
        {
            Assert.True(await enable.Content.ReadFromJsonAsync<bool>());
        }

        await SeedGameAsync(db, older, "Older Game", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedGameAsync(db, newer, "Newer Game", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        using (var watch = await api.Client.PostAsJsonAsync("/api/internal/watch",
                new WatchRequest(user, [ThreadUrl(older), ThreadUrl(newer)], false)))
        {
            Assert.Equal(HttpStatusCode.OK, watch.StatusCode); // 200 even on failure: the body carries the message
            var dto = await watch.Content.ReadFromJsonAsync<WatchResponse>();
            Assert.NotNull(dto);
            Assert.True(dto!.Success);
            Assert.Contains("Games added", dto.Response);
        }

        using (var list = await api.Client.GetAsync($"/api/internal/watch/{user}"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var games = await list.Content.ReadFromJsonAsync<List<InternalGameDto>>();
            Assert.NotNull(games);
            // LastUpdated descending, like the bot's own ordering
            Assert.Equal([("Newer Game", $"https://f95zone.to/threads/{newer}"),
                          ("Older Game", $"https://f95zone.to/threads/{older}")],
                         games!.Select(g => (g.Title, g.Url)).ToList());
        }

        using (var unwatch = await api.Client.PostAsJsonAsync("/api/internal/unwatch",
                new WatchRequest(user, [ThreadUrl(older)], false)))
        {
            Assert.Equal(HttpStatusCode.OK, unwatch.StatusCode);
            var dto = await unwatch.Content.ReadFromJsonAsync<WatchResponse>();
            Assert.True(dto!.Success);
        }

        using (var after = await api.Client.GetAsync($"/api/internal/watch/{user}"))
        {
            var games = await after.Content.ReadFromJsonAsync<List<InternalGameDto>>();
            var remaining = Assert.Single(games!);
            Assert.Equal($"https://f95zone.to/threads/{newer}", remaining.Url);
        }
    }

    // ---- link consume -------------------------------------------------------------------------

    [Fact]
    public async Task LinkConsume_ValidCode_MergesDiscordUserIntoWebAccount()
    {
        const ulong user = UserIdBase + 61;
        AuthResult web;
        string code;
        await using (var seed = db.CreateContext())
        {
            var service = new WebAuthService(seed, NullLogger<WebAuthService>.Instance);
            web = await service.RegisterAsync("link-api-happy", "password123");
            Assert.True(web.Success);
            code = (await service.CreateLinkCodeAsync(web.AccountId))!.Value.Code;
        }

        using (var enable = await api.Client.PostAsJsonAsync("/api/internal/users",
                                                             new AddUserRequest(user, "link-user")))
        {
            Assert.True(await enable.Content.ReadFromJsonAsync<bool>());
        }

        using var consume = await api.Client.PostAsJsonAsync("/api/internal/link/consume",
                                                             new LinkConsumeRequest(code, user, "link-user"));

        Assert.Equal(HttpStatusCode.OK, consume.StatusCode);
        var dto = await consume.Content.ReadFromJsonAsync<LinkConsumeResponse>();
        Assert.Equal(LinkOutcomeKind.Merged, dto!.Kind);

        await using var verify = db.CreateContext();
        var linked = await verify.Users.SingleAsync(u => u.UserId == user);
        Assert.Equal(web.AccountId, linked.AccountId);
    }

    [Fact]
    public async Task LinkConsume_InvalidCode_IsKindInvalid_NotServerError()
    {
        const ulong user = UserIdBase + 71;
        using (var enable = await api.Client.PostAsJsonAsync("/api/internal/users",
                                                             new AddUserRequest(user, "link-invalid")))
        {
            Assert.True(await enable.Content.ReadFromJsonAsync<bool>());
        }

        using var consume = await api.Client.PostAsJsonAsync("/api/internal/link/consume",
            new LinkConsumeRequest("NO-SUCH-CODE-AT-ALL", user, "link-invalid"));

        // an unknown code is a normal outcome (Kind=Invalid), not the 500 exception path
        Assert.Equal(HttpStatusCode.OK, consume.StatusCode);
        var dto = await consume.Content.ReadFromJsonAsync<LinkConsumeResponse>();
        Assert.Equal(LinkOutcomeKind.Invalid, dto!.Kind);
        Assert.False(string.IsNullOrEmpty(dto.Message));
    }
}
