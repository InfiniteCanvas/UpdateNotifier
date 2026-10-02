using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Abstractions;
using UpdateNotifier.Extensions;
using UpdateNotifier.Services;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Tests;

/// <summary>
///     The API container always composes the core now: DMs are enqueued durably as
///     <see cref="QueuedDmSender" /> rows for the bot container to poll, and privilege checks read
///     the bot-synced cache through <see cref="SyncedPrivilegeChecker" />. Only references
///     UpdateNotifier.Core - proving the core is testable without Discord.Net - and last-wins
///     registrations still replace the defaults for embedders that bring their own pieces.
/// </summary>
[Collection(DatabaseFixture.CollectionName)]
public sealed class HeadlessCompositionTests
{
    [Fact]
    public void CoreRegistrations_ResolveQueuedSenderAndSyncedPrivileges()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUpdateNotifierCore();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<QueuedDmSender>(provider.GetRequiredService<IDmSender>());
        Assert.IsType<SyncedPrivilegeChecker>(provider.GetRequiredService<IPrivilegeChecker>());
    }

    [Fact]
    public void CoreRegistrations_ResolveThumbnailServicesAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUpdateNotifierCore();

        using var provider = services.BuildServiceProvider();

        var queue = provider.GetRequiredService<ThumbnailQueue>();
        var scraper = provider.GetRequiredService<ThumbnailScraperService>();

        Assert.IsType<ThumbnailQueue>(queue);
        Assert.IsType<ThumbnailScraperService>(scraper);
        Assert.Same(queue, provider.GetRequiredService<ThumbnailQueue>());
        Assert.Same(scraper, provider.GetRequiredService<ThumbnailScraperService>());
        // the hosted registration hands out the same singleton, not a second instance
        Assert.Contains(scraper, provider.GetServices<IHostedService>());
    }

    [Fact]
    public void LastWinsRegistration_ReplacesQueuedSenderAndSyncedChecker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUpdateNotifierCore();
        // an embedder overriding after AddUpdateNotifierCore must still win (last registration takes it)
        services.AddSingleton<IDmSender, NoopDmSender>();
        services.AddSingleton<IPrivilegeChecker>(provider => new InlinePrivilegeChecker(provider.GetRequiredService<Config>().SelfHosted));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<NoopDmSender>(provider.GetRequiredService<IDmSender>());
        Assert.IsType<InlinePrivilegeChecker>(provider.GetRequiredService<IPrivilegeChecker>());
    }
}
