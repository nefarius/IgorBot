using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

using IgorBot.Services;
using IgorBot.Util;

namespace IgorBot.Modules;

internal sealed class IgorCoreModule(
    ILogger<IgorCoreModule> logger,
    IDiscordReadinessService readiness,
    ILurkerReconciliationService reconciliation,
    IHostApplicationLifetime lifetime)
    : IEventHandler<GuildDownloadCompletedEventArgs>, IEventHandler<GuildAvailableEventArgs>
{
    public Task HandleEventAsync(DiscordClient sender, GuildDownloadCompletedEventArgs args)
    {
        foreach (DiscordGuild guild in args.Guilds.Values)
        {
            readiness.MarkGuildAvailable(guild.Id);
            logger.LogInformation("{Guild} downloaded", guild);
            TriggerReconciliation(guild.Id);
        }

        return Task.CompletedTask;
    }

    public Task HandleEventAsync(DiscordClient sender, GuildAvailableEventArgs args)
    {
        readiness.MarkGuildAvailable(args.Guild.Id);
        logger.LogInformation("{Guild} online", args.Guild);
        TriggerReconciliation(args.Guild.Id);
        return Task.CompletedTask;
    }

    // Per-guild scans are single-flight, so the overlapping download/available events are harmless.
    private void TriggerReconciliation(ulong guildId)
    {
        ObservableBackgroundWork.Run(
            ct => reconciliation.ReconcileGuildAsync(guildId, ct),
            lifetime.ApplicationStopping,
            logger,
            "lurker-reconciliation",
            cancelInFlightOnStop: true);
    }
}
