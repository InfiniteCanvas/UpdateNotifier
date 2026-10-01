using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UpdateNotifier.Abstractions;
using UpdateNotifier.Extensions;
using UpdateNotifier.Utilities;

namespace UpdateNotifier.Tests;

/// <summary>
///     Mirrors the host's headless branch: the core wires up on its own, Discord is replaced by no-ops.
///     Only references UpdateNotifier.Core - proving the core is testable without Discord.Net.
/// </summary>
[Collection(DatabaseFixture.CollectionName)]
public sealed class HeadlessCompositionTests
{
	[Fact]
	public void HeadlessRegistrations_ResolveNoopSenderAndInlinePrivileges()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddUpdateNotifierCore();
		services.AddSingleton<IDmSender, NoopDmSender>();
		services.AddSingleton<IPrivilegeChecker>(provider => new InlinePrivilegeChecker(provider.GetRequiredService<Config>().SelfHosted));

		using var provider = services.BuildServiceProvider();

		Assert.IsType<NoopDmSender>(provider.GetRequiredService<IDmSender>());
		Assert.IsType<InlinePrivilegeChecker>(provider.GetRequiredService<IPrivilegeChecker>());
	}
}
