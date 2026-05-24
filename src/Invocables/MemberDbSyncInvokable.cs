using Coravel.Invocable;

using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;
using IgorBot.Util;

using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting;

namespace IgorBot.Invocables;

/// <summary>
///     Scheduled job to synchronize the database to the Discord universe objects.
/// </summary>
internal class MemberDbSyncInvokable(
    DB db,
    IGuildConfigService guildConfigService,
    IDiscordClientService discord,
    ILogger<MemberDbSyncInvokable> logger)
    : IInvocable
{
    public async Task Invoke()
    {
        logger.LogInformation("Running members database synchronization");

        try
        {
            IReadOnlyList<GuildConfig> guildConfigs = await guildConfigService.GetAllAsync();
            foreach (GuildConfig guildConfig in guildConfigs)
            {
                if (!discord.Client.Guilds.TryGetValue(guildConfig.GuildId, out DiscordGuild? guild))
                {
                    logger.LogWarning("Guild {GuildId} not present in client, skipping sync", guildConfig.GuildId);
                    continue;
                }

                logger.LogDebug("Processing members of {Guild}", guild);

                IReadOnlyCollection<DiscordMember> members = await guild.GetAllMembersAsync();

                foreach (DiscordMember member in members.Where(m => !m.IsBot))
                {
                    string id = member.ToEntityId();
                    GuildMember? guildMember = await db.Find<GuildMember>().OneAsync(id);

                    // already exists
                    if (guildMember is not null)
                    {
                        // newbie channel object exists in DB but no longer in guild
                        if (guildMember.Channel is not null &&
                            !guild.Channels.ContainsKey(guildMember.Channel.ChannelId))
                        {
                            logger.LogInformation("Removing orphaned channel entity {Channel}", guildMember.Channel);
                            await guildMember.DeleteChannel(db);
                        }

                        string currentMemberString = member.ToString();

                        if (!string.IsNullOrEmpty(currentMemberString) && guildMember.Member != currentMemberString)
                        {
                            logger.LogInformation("Updating Member property from {Old} to {New} for entity {Member}",
                                guildMember.Member, currentMemberString, guildMember);
                            guildMember.Member = currentMemberString;
                            await db.SaveAsync(guildMember);
                        }

                        continue;
                    }

                    // add missing
                    DateTime now = DateTime.UtcNow;
                    guildMember = new GuildMember
                    {
                        GuildId = member.Guild.Id,
                        MemberId = member.Id,
                        Member = member.ToString(),
                        Mention = member.Mention,
                        Status = MemberStatus.New,
                        StatusChangedAt = now,
                        StatusReason = "discovered_by_sync"
                    };
                    guildMember.StatusHistory.Add(new MemberStatusEvent
                    {
                        From = MemberStatus.Unknown,
                        To = MemberStatus.New,
                        At = now,
                        Reason = "discovered_by_sync"
                    });

                    await db.SaveAsync(guildMember);

                    logger.LogInformation("{Member} added to DB by sync (stamped Status={Status})", member,
                        MemberStatus.New);
                }
            }

            logger.LogInformation("Members database synchronization done");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occured during sync run");
        }
    }
}