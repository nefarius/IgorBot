using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Util;

using MongoDB.Entities;

namespace IgorBot.Services;

internal sealed class MemberPromotionService(DB db, ILogger<MemberPromotionService> logger) : IMemberPromotionService
{
    public async Task PromoteAsync(
        DiscordGuild guild,
        DiscordMember member,
        GuildMember entry,
        GuildConfig guildConfig,
        ulong? actorId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DiscordRole strangerRole = guild.Roles[guildConfig.StrangerRoleId];
        DiscordRole memberRole = guild.Roles[guildConfig.MemberRoleId];

        // Perform Discord mutations before persisting so the DB is only updated
        // when both role operations have actually succeeded.
        await member.GrantRoleAsync(memberRole);

        try
        {
            await member.RevokeRoleAsync(strangerRole);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RevokeRoleAsync failed for {Member} after GrantRoleAsync succeeded; " +
                                "member role has been granted but stranger role was not removed", member);
            throw;
        }

        // Record FullMember only from the status just loaded. A compare-and-set against every
        // promotable status would accept a newer write and store this snapshot as the history "from".
        // On that conflict, reload and retry once from the refreshed status.
        bool recorded = false;
        for (int attempt = 0; attempt < 2 && !recorded; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GuildMember? current = await db.Find<GuildMember>().OneAsync(entry.ID);
            if (current is null)
            {
                break;
            }

            if (current.Status == MemberStatus.FullMember)
            {
                recorded = true;
                break;
            }

            IReadOnlyCollection<MemberStatus>? promotableFrom =
                MemberTransitionPolicy.AllowedFrom(MemberStatus.FullMember);
            if (promotableFrom is null || !promotableFrom.Contains(current.Status))
            {
                break;
            }

            recorded = await current.TryTransitionAsync(
                db,
                MemberStatus.FullMember,
                allowedFrom: [current.Status],
                reason: reason,
                actorId: actorId);
        }

        if (!recorded)
        {
            logger.LogWarning(
                "Promotion of {Member} did not update the status: persisted status is no longer promotable",
                member);
        }
        if (actorId.HasValue)
        {
            logger.LogInformation("{ActorId} promoted {Member}", actorId, member);
        }
        else
        {
            logger.LogInformation("Automatic promotion of {Member}: {Reason}", member, reason);
        }

        if (!string.IsNullOrEmpty(guildConfig.MemberWelcomeTemplate))
        {
            try
            {
                DiscordChannel? welcomeChannel = guild.FindChannel(guildConfig.MemberWelcomeMessageChannelId);
                if (welcomeChannel is null)
                {
                    logger.LogWarning(
                        "Member welcome channel {ChannelId} is not cached for guild {GuildId}",
                        guildConfig.MemberWelcomeMessageChannelId, guild.Id);
                }
                else
                {
                    await welcomeChannel.SendMessageAsync(
                        string.Format(guildConfig.MemberWelcomeTemplate, member.Mention));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to deliver welcome message");
            }
        }
        else
        {
            logger.LogWarning("MemberWelcomeTemplate is not configured for guild {GuildId}, skipping welcome message",
                guild.Id);
        }
    }
}