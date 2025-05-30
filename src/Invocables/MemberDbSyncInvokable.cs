using Coravel.Invocable;

using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Util;

using Microsoft.Extensions.Options;

using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting;

namespace IgorBot.Invocables;

internal class MemberDbSyncInvokable(
    IOptionsMonitor<IgorConfig> config,
    IDiscordClientService discord,
    ILogger<MemberDbSyncInvokable> logger)
    : IInvocable
{
    public async Task Invoke()
    {
        logger.LogInformation("Running members database synchronization");

        foreach (GuildConfig config1 in config.CurrentValue.Guilds.Select(gc => gc.Value))
        {
            DiscordGuild guild = discord.Client.Guilds[config1.GuildId];

            logger.LogDebug("Processing members of {Guild}", guild);

            IReadOnlyCollection<DiscordMember> members = await guild.GetAllMembersAsync();

            foreach (DiscordMember member in members.Where(m => !m.IsBot))
            {
                string id = member.ToEntityId();
                GuildMember guildMember = await DB.Find<GuildMember>().OneAsync(id);

                // already exists
                if (guildMember is not null)
                {
                    // newbie channel object exists in DB but no longer in guild
                    if (guildMember.Channel is not null && !guild.Channels.ContainsKey(guildMember.Channel.ChannelId))
                    {
                        logger.LogInformation("Removing orphaned channel entity {Channel}", guildMember.Channel);
                        await guildMember.DeleteChannel();
                    }

                    continue;
                }

                // add missing
                guildMember = new GuildMember
                {
                    GuildId = member.Guild.Id,
                    MemberId = member.Id,
                    Member = member.ToString(),
                    Mention = member.Mention
                };

                await guildMember.SaveAsync();

                logger.LogInformation("{Member} added to DB", member);
            }
        }

        logger.LogInformation("Members database synchronization done");
    }
}