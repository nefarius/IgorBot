using Coravel.Invocable;

using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;
using IgorBot.Util;

using MongoDB.Entities;

namespace IgorBot.Invocables;

/// <summary>
///     Scheduled job to synchronize the database to the Discord universe objects.
/// </summary>
internal class MemberDbSyncInvokable(
    DB db,
    IGuildConfigService guildConfigService,
    DiscordClient discord,
    ILogger<MemberDbSyncInvokable> logger)
    : IInvocable
{
    public Task Invoke() => SingleFlight.RunAsync(nameof(MemberDbSyncInvokable), InvokeCore, logger);

    private async Task InvokeCore()
    {
        logger.LogInformation("Running members database synchronization");

        try
        {
            IReadOnlyList<GuildConfig> guildConfigs = await guildConfigService.GetAllAsync();
            foreach (GuildConfig guildConfig in guildConfigs)
            {
                if (!discord.Guilds.TryGetValue(guildConfig.GuildId, out DiscordGuild? guild))
                {
                    logger.LogWarning("Guild {GuildId} not present in client, skipping sync", guildConfig.GuildId);
                    continue;
                }

                logger.LogDebug("Processing members of {Guild}", guild);

                List<DiscordMember> members;
                try
                {
                    members = await guild.GetAllMembersAsync().MaterializeAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to list members of {Guild}; continuing with remaining guilds", guild);
                    continue;
                }

                foreach (DiscordMember member in members.Where(m => !m.IsBot))
                {
                    // One bad member must not stop the synchronization of everybody else.
                    try
                    {
                        await SyncMemberAsync(guild, member);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to synchronize {Member}", member);
                    }
                }
            }

            logger.LogInformation("Members database synchronization done");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occured during sync run");
        }
    }

    private async Task SyncMemberAsync(DiscordGuild guild, DiscordMember member)
    {
        DateTime now = DateTime.UtcNow;

        // Atomic create-or-load; never overwrites a document another handler is creating or changing.
        (GuildMember guildMember, bool created) = await GuildMember.EnsureExistsAsync(
            db, guild.Id, member.Id, member.ToString(), member.Mention,
            MemberStatus.New, MemberLifecycleClassifier.DiscoveredBySyncReason,
            new MemberStatusEvent
            {
                From = MemberStatus.Unknown,
                To = MemberStatus.New,
                At = now,
                Reason = MemberLifecycleClassifier.DiscoveredBySyncReason
            });

        if (created)
        {
            logger.LogInformation("{Member} added to DB by sync (stamped Status={Status})", member,
                MemberStatus.New);
            return;
        }

        // newbie channel object exists in DB but no longer in guild
        if (guildMember.Channel is not null &&
            !guild.Channels.ContainsKey(guildMember.Channel.ChannelId))
        {
            // A cache miss is not proof of deletion; confirm through the REST API first.
            bool confirmedGone = false;
            try
            {
                await guild.GetChannelAsync(guildMember.Channel.ChannelId);
            }
            catch (NotFoundException)
            {
                confirmedGone = true;
            }

            if (confirmedGone)
            {
                logger.LogInformation("Removing orphaned channel entity {Channel}", guildMember.Channel);
                await guildMember.DeleteChannel(db);
            }
        }

        string currentMemberString = member.ToString();

        if (!string.IsNullOrEmpty(currentMemberString) && guildMember.Member != currentMemberString)
        {
            logger.LogInformation("Updating Member property from {Old} to {New} for entity {Member}",
                guildMember.Member, currentMemberString, guildMember);
            await guildMember.UpdateDisplayAsync(db, currentMemberString, guildMember.Mention);
        }
    }
}
