using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

using IgorBot.Schema;
using IgorBot.Util;

using MongoDB.Entities;

namespace IgorBot.Modules;

internal partial class ApplicationWorkflow
{
    //
    // Called when member left the Guild
    // 
    public async Task DiscordOnGuildMemberRemoved(DiscordClient sender, GuildMemberRemoveEventArgs e)
    {
        if (e.Member.IsBot)
        {
            return;
        }

        if (!config.CurrentValue.Guilds.ContainsKey(e.Guild.Id.ToString()))
        {
            return;
        }

        logger.LogInformation("{Member} left", e.Member);

        GuildMember member = await DB.Find<GuildMember>().OneAsync(e.ToEntityId());

        if (member is null)
        {
            logger.LogWarning("{Member} not found in DB", e.Member);
            return;
        }

        if (member.Channel is null)
        {
            logger.LogInformation("{Member} has no newbie channel", e.Member);
            return;
        }

        _ = Task.Run(async () =>
        {
            member.LeftAt = DateTime.UtcNow;
            await member.SaveAsync();

            // Remove newbie channel
            NewbieChannel newbieChannel = member.Channel;

            if (newbieChannel is not null)
            {
                try
                {
                    DiscordChannel discordChannel = e.Guild.GetChannel(newbieChannel.ChannelId);

                    logger.LogInformation("Removing channel {Channel}", discordChannel);

                    await discordChannel.DeleteAsync($"{e.Member} has been removed");

                    await member.DeleteChannel();
                }
                catch (ServerErrorException)
                {
                    logger.LogWarning("Couldn't find temporary channel to delete");
                }
            }

            StrangerApplicationEmbed application = member.Application;

            if (application is not null)
            {
                try
                {
                    if (member.RemovedByModeration)
                    {
                        logger.LogWarning("{Member} left due to moderator action", e.Member);
                        return;
                    }

                    logger.LogInformation(
                        member.AutoKickedAt.HasValue
                            ? "{Member} removed due to idle timeout"
                            : "{Member} left by themselves", e.Member);

                    await member.DeleteApplicationWidget(sender);

                    await member.DeleteApplication();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update status message");
                }
            }
        });
    }
}