using System.Diagnostics.CodeAnalysis;

using Coravel.Invocable;

using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;

using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting;

namespace IgorBot.Invocables;

/// <summary>
///     Scheduled task to query for stale users and kick them.
/// </summary>
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal class KickStaleInvokable(
    DB db,
    ILogger<KickStaleInvokable> logger,
    IGuildConfigService guildConfigService,
    IDiscordClientService discord,
    IDiscordReadinessService readiness)
    : IInvocable
{
    public async Task Invoke()
    {
        logger.LogDebug("Running stale kick timer");

        IReadOnlyList<GuildConfig> allConfigs = await guildConfigService.GetAllAsync();

        // Enumerate guild configs with an active idle timespan set
        foreach (GuildConfig config1 in allConfigs.Where(gc => gc.IdleKickTimeSpan.HasValue))
        {
            if (!discord.Client.Guilds.TryGetValue(config1.GuildId, out DiscordGuild guild))
            {
                // If GuildAvailable has never fired for this guild the client cache is still
                // being populated (startup race). Log at Debug to avoid false-alarm warnings.
                // Once the guild has been seen at least once, a missing entry is a real problem.
                if (readiness.IsGuildReady(config1.GuildId))
                {
                    logger.LogWarning("Guild {GuildId} not present in client, skipping stale kick", config1.GuildId);
                }
                else
                {
                    logger.LogDebug("Guild {GuildId} not yet available in client (startup), skipping stale kick",
                        config1.GuildId);
                }

                continue;
            }

            // Query for members that are in the Onboarding state, have auto-kick enabled,
            // and whose application window has expired.
            // QuestionnaireSubmittedAt == null is a hard guard: members in the
            // QuestionnaireSubmitted status always have a non-null QuestionnaireSubmittedAt
            // and are therefore excluded from this query.
            // For un-migrated documents (Status == Unknown) the legacy timestamp fields
            // (PromotedAt, StrangerRoleRemovedAt, FullMemberAt) serve as a fallback gate.
            List<GuildMember> staleMembers = await db.Find<GuildMember>()
                .ManyAsync(m =>
                    m.Lt(f => f.Application.CreatedAt, DateTime.UtcNow.Add(-config1.IdleKickTimeSpan!.Value)) &
                    m.Eq(f => f.GuildId, config1.GuildId) &
                    m.Eq(f => f.Application.IsAutoKickEnabled, true) &
                    m.Eq(f => f.Application.QuestionnaireSubmittedAt, null) &
                    (
                        m.Eq(f => f.Status, MemberStatus.Onboarding) |
                        (
                            m.Eq(f => f.Status, MemberStatus.Unknown) &
                            m.Eq(f => f.PromotedAt, null) &
                            m.Eq(f => f.StrangerRoleRemovedAt, null) &
                            m.Eq(f => f.FullMemberAt, null)
                        )
                    ) &
                    m.Eq(f => f.AutoKickedAt, null)
                );

            logger.LogDebug("Running stale members check for {Guild}", guild);

            if (staleMembers.Count == 0)
            {
                logger.LogDebug("No stale members found");
            }

            foreach (GuildMember guildMember in staleMembers)
            {
                logger.LogInformation(
                    "Initiating auto-kick of stale member {MemberId} (current status {Status}, widget created {CreatedAt})",
                    guildMember.MemberId, guildMember.Status, guildMember.Application?.CreatedAt);

                DiscordMember member;

                try
                {
                    member = await guild.GetMemberAsync(guildMember.MemberId);
                }
                catch (NotFoundException)
                {
                    logger.LogWarning("Member {MemberId} already left the guild, marking as auto-kicked",
                        guildMember.MemberId);
                    await guildMember.TransitionToAsync(db, MemberStatus.AutoKicked, "idle timeout (already left)");
                    continue;
                }

                // Pre-mark in DB so that the GuildMemberRemoved event that fires immediately
                // after RemoveAsync sees AutoKicked and does not mis-classify the departure
                // as a voluntary leave (mirroring HandleStrangerKick / HandleStrangerBan).
                MemberStatus previousStatus = guildMember.Status;
                await guildMember.TransitionToAsync(db, MemberStatus.AutoKicked, "idle timeout");

                try
                {
                    logger.LogInformation("Calling RemoveAsync for stale member {MemberId}", guildMember.MemberId);
                    await member.RemoveAsync("Member removed due to idle timeout");

                    logger.LogInformation("Auto-kicked stale member {MemberId} due to idle timeout", guildMember.MemberId);
                }
                catch (NotFoundException)
                {
                    // Member left between GetMemberAsync and RemoveAsync; DB is already correct.
                    logger.LogWarning("Member {MemberId} left during auto-kick, DB already marked as auto-kicked",
                        guildMember.MemberId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to auto-remove {MemberId}, rolling back status to {Previous}",
                        guildMember.MemberId, previousStatus);

                    // Before rolling back, confirm the member is still in the guild.
                    // RemoveAsync can fail after Discord already processed the kick (e.g. a
                    // transient 5xx), in which case the pre-marked AutoKicked status is correct
                    // and must not be overwritten. Only roll back when the member is confirmed
                    // still present.
                    bool memberStillPresent;
                    try
                    {
                        await guild.GetMemberAsync(guildMember.MemberId);
                        memberStillPresent = true;
                    }
                    catch (NotFoundException)
                    {
                        // Kick went through despite the error; DB is already correct.
                        logger.LogWarning(
                            "Member {MemberId} is no longer in the guild after failed RemoveAsync; keeping AutoKicked status",
                            guildMember.MemberId);
                        memberStillPresent = false;
                    }
                    catch (Exception confirmEx)
                    {
                        // Cannot confirm membership; leave AutoKicked in place to avoid
                        // re-opening the member for a duplicate kick on the next tick.
                        logger.LogError(confirmEx,
                            "Could not confirm membership for {MemberId} after failed RemoveAsync; keeping AutoKicked status",
                            guildMember.MemberId);
                        memberStillPresent = false;
                    }

                    if (memberStillPresent)
                    {
                        try
                        {
                            await guildMember.TransitionToAsync(db, previousStatus, "rollback after failed auto-kick");
                        }
                        catch (Exception rollbackEx)
                        {
                            logger.LogError(rollbackEx,
                                "Rollback to {Previous} failed for {MemberId} after auto-kick failure",
                                previousStatus, guildMember.MemberId);
                        }
                    }
                }
            }
        }
    }
}