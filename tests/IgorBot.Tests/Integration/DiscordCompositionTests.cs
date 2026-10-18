using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;

using FluentAssertions;

using IgorBot.Core;
using IgorBot.Modules;
using IgorBot.Schema;
using IgorBot.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MongoDB.Entities;

namespace IgorBot.Tests.Integration;

[Xunit.Collection("Mongo")]
public sealed class DiscordCompositionTests(MongoFixture mongo)
{
    [Fact]
    public async Task Provider_ResolvesDiscordServices_WithoutConnecting()
    {
        DB db = await mongo.CreateDatabaseAsync(Guid.NewGuid().ToString("N"));
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<IGuildConfigService, UnusedGuildConfigService>();
        services.AddSingleton<IOnboardingQueue, UnusedOnboardingQueue>();
        services.AddSingleton<IHoneypotEnforcementService, UnusedHoneypotEnforcement>();
        services.AddSingleton<IHoneypotBanClient, UnusedHoneypotBanClient>();
        services.AddSingleton(SystemOneSettings.Create(new SystemOneConfig()));
        services.AddSingleton<IOnboardingDecisionClient, UnconfiguredOnboardingDecisionClient>();
        services.AddSingleton<IOnboardingDecisionService, OnboardingDecisionService>();
        services.AddSingleton<ISystemOneProbe, SystemOneProbe>();
        services.AddSingleton<IOnboardingAutomationExecutor, OnboardingAutomationExecutor>();
        services.AddSingleton<IMemberPromotionService, UnusedPromotionService>();
        services.AddSingleton<IDiscordReadinessService, DiscordReadinessService>();
        services.AddSingleton<IHostApplicationLifetime, IdleHostLifetime>();
        IgorDiscordRegistration.AddIgorDiscord(services, "composition-test-token");

        ServiceProvider provider = services.BuildServiceProvider();
        try
        {
            provider.GetRequiredService<DiscordClient>().Should().NotBeNull();
            provider.GetRequiredService<CommandsExtension>().Should().NotBeNull();
            provider.GetRequiredService<InteractivityExtension>().Should().NotBeNull();
            provider.GetRequiredService<ApplicationWorkflow>().Should().NotBeNull();
            provider.GetRequiredService<HoneypotModule>().Should().NotBeNull();
            provider.GetRequiredService<AuditLogKickHandler>().Should().NotBeNull();
            provider.GetRequiredService<IgorCoreModule>().Should().NotBeNull();
            provider.GetServices<IHostedService>().Should().Contain(service => service is DiscordGatewayHostedService);
        }
        finally
        {
            try
            {
                await provider.DisposeAsync();
            }
            catch (NullReferenceException)
            {
                // DSharpPlus 5.0.0-nightly-02613 disposes an unconnected gateway by
                // terminating a frame that was never opened. Resolution already succeeded.
            }
        }
    }

    private sealed class UnusedGuildConfigService : IGuildConfigService
    {
        public Task<GuildConfig?> GetAsync(ulong guildId, CancellationToken ct = default) =>
            Task.FromResult<GuildConfig?>(null);

        public Task<IReadOnlyList<GuildConfig>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GuildConfig>>([]);

        public Task SaveAsync(GuildConfig config, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class UnusedOnboardingQueue : IOnboardingQueue
    {
        public ValueTask EnqueueAsync(NewMemberMessage message, CancellationToken ct = default) => ValueTask.CompletedTask;
    }

    private sealed class UnusedHoneypotEnforcement : IHoneypotEnforcementService
    {
        public Task<HoneypotEnforcementOutcome> EnforceAsync(
            ulong guildId, ulong memberId, string memberDisplay, string mention) =>
            Task.FromResult(HoneypotEnforcementOutcome.Failed);
    }

    private sealed class IdleHostLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    private sealed class UnusedPromotionService : IMemberPromotionService
    {
        public Task PromoteAsync(
            DiscordGuild guild,
            DiscordMember member,
            GuildMember entry,
            GuildConfig guildConfig,
            ulong? actorId,
            string? reason,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class UnusedHoneypotBanClient : IHoneypotBanClient
    {
        public Task BanAsync(ulong guildId, ulong userId) => Task.CompletedTask;

        public Task<bool?> IsMemberPresentAsync(ulong guildId, ulong userId) => Task.FromResult<bool?>(null);
    }
}
