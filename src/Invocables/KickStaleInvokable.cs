using System.Diagnostics.CodeAnalysis;

using Coravel.Invocable;

using DSharpPlus.Entities;

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
            // query for members of the current guild where the application lifetime has exceeded the allowed idle time
            List<GuildMember> staleMembers = await db.Find<GuildMember>()
                .ManyAsync(m =>
                    m.Lt(f => f.Application.CreatedAt, DateTime.UtcNow.Add(-config1.IdleKickTimeSpan!.Value)) &
                    m.Eq(f => f.GuildId, config1.GuildId) &
                    m.Eq(f => f.Application.IsAutoKickEnabled, true) &
                    m.Eq(f => f.Application.QuestionnaireSubmittedAt, null) &
                    m.Eq(f => f.PromotedAt, null) &
                    m.Eq(f => f.StrangerRoleRemovedAt, null) &
                    m.Eq(f => f.FullMemberAt, null) &
                    m.Eq(f => f.AutoKickedAt, null)
                );

            DiscordGuild guild = discord.Client.Guilds[config1.GuildId];

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
                    guildMember.AutoKickedAt = DateTime.UtcNow;
                    await db.SaveAsync(guildMember);

                    try
                    {
                        DiscordMember member = await guild.GetMemberAsync(guildMember.MemberId);

                        await member.RemoveAsync("Member removed due to idle timeout");

                        logger.LogWarning("Removed {@Member} due to idle timeout", guildMember);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to auto-remove {@Member}", guildMember);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to remove guild member");
                }
            }
        }
    }
}