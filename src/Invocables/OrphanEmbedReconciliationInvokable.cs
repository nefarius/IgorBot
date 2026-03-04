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
///     Periodic job to clean up orphaned StrangerApplicationEmbed entities that have no matching GuildMember
///     or whose Discord message no longer exists.
/// </summary>
internal class OrphanEmbedReconciliationInvokable(
    DB db,
    IGuildConfigService guildConfigService,
    IDiscordClientService discord,
    ILogger<OrphanEmbedReconciliationInvokable> logger)
    : IInvocable
{
    /// <summary>
    ///     Grace period for newly-created embeds before treating as orphan (CreateApplicationWidget writes embed and GuildMember separately).
    /// </summary>
    private static readonly TimeSpan OrphanGracePeriod = TimeSpan.FromMinutes(2);

    public async Task Invoke()
    {
        logger.LogInformation("Running orphan application embed reconciliation");

        try
        {
            IReadOnlyList<GuildConfig> guildConfigs = await guildConfigService.GetAllAsync();

            foreach (GuildConfig guildConfig in guildConfigs)
            {
                if (!discord.Client.Guilds.TryGetValue(guildConfig.GuildId, out DiscordGuild guild))
                {
                    continue;
                }

                await ReconcileOrphanEmbedsAsync(guild);
                await ReconcileMissingDiscordMessagesAsync(guild);
            }

            logger.LogInformation("Orphan embed reconciliation done");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during orphan embed reconciliation");
        }
    }

    private async Task ReconcileOrphanEmbedsAsync(DiscordGuild guild)
    {
        List<StrangerApplicationEmbed> embeds = await db.Find<StrangerApplicationEmbed>()
            .ManyAsync(m => m.Eq(f => f.GuildId, guild.Id));

        foreach (StrangerApplicationEmbed embed in embeds)
        {
            if (DateTime.UtcNow - embed.CreatedAt < OrphanGracePeriod)
            {
                logger.LogDebug("Skipping embed {EmbedId} within grace period (created {CreatedAt})",
                    embed.ID, embed.CreatedAt);
                continue;
            }

            List<GuildMember> referencingMembers = await db.Find<GuildMember>()
                .ManyAsync(m => m.Eq(f => f.Application.ID, embed.ID));

            if (referencingMembers.Count == 0)
            {
                logger.LogInformation("Removing orphaned application embed {EmbedId} (no GuildMember references it)",
                    embed.ID);
                await db.DeleteAsync(embed);
            }
        }
    }

    private async Task ReconcileMissingDiscordMessagesAsync(DiscordGuild guild)
    {
        List<GuildMember> membersWithApplication = await db.Find<GuildMember>()
            .ManyAsync(m =>
                m.Eq(f => f.GuildId, guild.Id) &
                m.Ne(f => f.Application, null));

        foreach (GuildMember guildMember in membersWithApplication)
        {
            if (guildMember.Application is null)
            {
                continue;
            }

            try
            {
                DiscordChannel? channel = guild.GetChannel(guildMember.Application.ChannelId);
                if (channel is null)
                {
                    logger.LogInformation(
                        "Application widget channel no longer exists for {MemberId}, removing orphaned embed",
                        guildMember.MemberId);
                    await guildMember.DeleteApplication(db);
                    continue;
                }

                await channel.GetMessageAsync(guildMember.Application.MessageId);
            }
            catch (NotFoundException)
            {
                logger.LogInformation(
                    "Application widget message no longer exists for {MemberId}, removing orphaned embed",
                    guildMember.MemberId);
                await guildMember.DeleteApplication(db);
            }
        }
    }
}
