using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using Coravel.Queuing.Interfaces;

using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Entities;

using IgorBot.Invocables;

namespace IgorBot.ApplicationCommands;

/// <summary>
///     Scheduled or startup jobs an administrator can trigger on demand.
/// </summary>
public enum ManualJob
{
    [ChoiceDisplayName("Lurker reconciliation")]
    LurkerReconciliation,

    [ChoiceDisplayName("Member database sync")]
    MemberDbSync,

    [ChoiceDisplayName("Orphan embed reconciliation")]
    OrphanEmbedReconciliation,

    [ChoiceDisplayName("Honeypot reconciliation")]
    HoneypotReconciliation,

    [ChoiceDisplayName("Kick stale strangers")]
    KickStale
}

[Command("jobs"), Description("Run scheduled maintenance jobs on demand. Administrator only.")]
[RequirePermissions(DiscordPermission.Administrator)]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal sealed class JobCommands(IQueue queue, ILogger<JobCommands> logger)
{
    [Command("run"), Description("Queue a job to run now. It runs for all configured servers.")]
    public async Task Run(
        SlashCommandContext ctx,
        [Parameter("job"), Description("The job to run")]
        ManualJob job)
    {
        await ctx.DeferResponseAsync(true);

        switch (job)
        {
            case ManualJob.LurkerReconciliation:
                queue.QueueInvocable<LurkerReconciliationInvokable>();
                break;
            case ManualJob.MemberDbSync:
                queue.QueueInvocable<MemberDbSyncInvokable>();
                break;
            case ManualJob.OrphanEmbedReconciliation:
                queue.QueueInvocable<OrphanEmbedReconciliationInvokable>();
                break;
            case ManualJob.HoneypotReconciliation:
                queue.QueueInvocable<HoneypotReconciliationInvokable>();
                break;
            case ManualJob.KickStale:
                queue.QueueInvocable<KickStaleInvokable>();
                break;
            default:
                await ctx.EditResponseAsync("Unknown job.");
                return;
        }

        logger.LogInformation("{User} queued job {Job} manually", ctx.User, job);
        await ctx.EditResponseAsync($"Queued **{job}**. Progress and results are in the bot log.");
    }
}
