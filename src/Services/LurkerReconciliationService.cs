using DSharpPlus;
using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Util;

using MongoDB.Entities;

namespace IgorBot.Services;

internal interface ILurkerReconciliationService
{
    Task ReconcileAllAsync(CancellationToken ct = default);

    Task ReconcileGuildAsync(ulong guildId, CancellationToken ct = default);
}

/// <summary>
///     Self-healing: finds members holding only the Stranger (Lurker) role that have neither a newbie channel nor
///     a status widget (e.g. they joined while the bot was offline) and submits them to the onboarding workflow.
/// </summary>
internal sealed class LurkerReconciliationService(
    DB db,
    DiscordClient discord,
    IGuildConfigService guildConfigService,
    IOnboardingSubmitter submitter,
    IDiscordReadinessService readiness,
    ILogger<LurkerReconciliationService> logger) : ILurkerReconciliationService
{
    /// <summary>
    ///     Upper bound of members queued per guild and scan, so a large backlog can't flood the Discord API with
    ///     channel creations. Remaining members are picked up by the next scan (or <c>/jobs run</c>).
    /// </summary>
    internal const int MaxSubmissionsPerScan = 50;

    private readonly Dictionary<ulong, SemaphoreSlim> _locks = new();

    public async Task ReconcileAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<GuildConfig> configs = await guildConfigService.GetAllAsync(ct);
        foreach (GuildConfig config in configs)
        {
            ct.ThrowIfCancellationRequested();
            await ReconcileGuildAsync(config.GuildId, ct);
        }
    }

    public async Task ReconcileGuildAsync(ulong guildId, CancellationToken ct = default)
    {
        SemaphoreSlim gate;
        lock (_locks)
        {
            if (!_locks.TryGetValue(guildId, out gate!))
            {
                gate = _locks[guildId] = new SemaphoreSlim(1, 1);
            }
        }

        // Skip if a scan for this guild is already running.
        if (!await gate.WaitAsync(0, ct))
        {
            logger.LogDebug("Lurker reconciliation already running for guild {GuildId}", guildId);
            return;
        }

        try
        {
            await ReconcileCoreAsync(guildId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lurker reconciliation failed for guild {GuildId}", guildId);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ReconcileCoreAsync(ulong guildId, CancellationToken ct)
    {
        GuildConfig? config = await guildConfigService.GetAsync(guildId, ct);
        if (config is null || !config.EnableOnboardingWorkflow || config.StrangerRoleId == 0 ||
            config.StrangerStatusChannelId == 0 || config.ApplicationCategoryId == 0)
        {
            return;
        }

        if (!discord.Guilds.TryGetValue(guildId, out DiscordGuild? guild))
        {
            if (readiness.IsGuildReady(guildId))
            {
                logger.LogWarning("Guild {GuildId} not present in client, skipping lurker reconciliation", guildId);
            }

            return;
        }

        if (guild.FindChannel(config.StrangerStatusChannelId) is null ||
            guild.FindChannel(config.ApplicationCategoryId) is null ||
            !guild.Roles.ContainsKey(config.StrangerRoleId))
        {
            logger.LogWarning("Guild {GuildId} onboarding resources missing, skipping lurker reconciliation",
                guildId);
            return;
        }

        logger.LogInformation("Running lurker reconciliation for {Guild}", guild);

        List<DiscordMember> members = await guild.GetAllMembersAsync().MaterializeAsync();
        int submitted = 0;

        foreach (DiscordMember member in members)
        {
            ct.ThrowIfCancellationRequested();

            if (submitted >= MaxSubmissionsPerScan)
            {
                logger.LogWarning(
                    "Lurker reconciliation for {Guild} hit the cap of {Cap} submissions, remaining members wait for the next scan",
                    guild, MaxSubmissionsPerScan);
                break;
            }

            if (!LurkerReconciliationPolicy.HasOnlyStrangerRole(
                    member.IsBot, member.Roles.Select(r => r.Id).ToList(), config.StrangerRoleId))
            {
                continue;
            }

            try
            {
                GuildMember? dbMember = await db.Find<GuildMember>().OneAsync(member.ToEntityId());

                if (dbMember is null)
                {
                    DateTime now = DateTime.UtcNow;
                    dbMember = new GuildMember
                    {
                        GuildId = guild.Id,
                        MemberId = member.Id,
                        Member = member.ToString(),
                        Mention = member.Mention,
                        Status = MemberStatus.New,
                        StatusChangedAt = now,
                        StatusReason = "reconciliation"
                    };
                    await db.SaveAsync(dbMember);
                }

                if (!LurkerReconciliationPolicy.NeedsOnboarding(dbMember))
                {
                    continue;
                }

                logger.LogInformation("Reconciliation: {Member} is missing channel or widget, submitting", member);
                await submitter.SubmitAsync(guild, config, dbMember, ct);
                submitted++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lurker reconciliation failed for {Member}", member);
            }
        }

        logger.LogInformation("Lurker reconciliation for {Guild} done, submitted {Count}", guild, submitted);
    }
}

/// <summary>
///     Pure eligibility rules, mirroring the live <c>ApplicationWorkflow</c> gates.
/// </summary>
internal static class LurkerReconciliationPolicy
{
    public static bool HasOnlyStrangerRole(bool isBot, IReadOnlyCollection<ulong> roleIds, ulong strangerRoleId)
    {
        return !isBot && roleIds.Count == 1 && roleIds.Contains(strangerRoleId);
    }

    public static bool NeedsOnboarding(GuildMember member)
    {
        // Partial records (channel without widget or vice versa) are repaired too; the onboarding
        // handler only recreates the missing artifact.
        return (member.Channel is null || member.Application is null) && !member.IsOnboardingInProgress;
    }
}
