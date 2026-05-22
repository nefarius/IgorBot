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
    IDiscordClientService discord)
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
                logger.LogWarning("Guild {GuildId} not present in client, skipping stale kick", config1.GuildId);
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
                        m.In(f => f.Status, new[]
                        {
                            MemberStatus.Unknown, MemberStatus.Onboarding, MemberStatus.QuestionnaireSubmitted
                        }) |
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
                logger.LogInformation("Processing stale member {MemberId}", guildMember.MemberId);

                try
                {
                    DiscordMember member = await guild.GetMemberAsync(guildMember.MemberId);

                    await member.RemoveAsync("Member removed due to idle timeout");

                    logger.LogWarning("Removed {@Member} due to idle timeout", guildMember);

                    await guildMember.TransitionToAsync(db, MemberStatus.AutoKicked, "idle timeout");
                }
                catch (NotFoundException)
                {
                    logger.LogWarning("Member {MemberId} already left the guild, marking as auto-kicked",
                        guildMember.MemberId);
                    await guildMember.TransitionToAsync(db, MemberStatus.AutoKicked, "idle timeout (already left)");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to auto-remove {MemberId}", guildMember.MemberId);
                }
            }
        }
    }
}