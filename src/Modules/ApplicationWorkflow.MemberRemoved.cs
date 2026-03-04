using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

using IgorBot.Schema;
using IgorBot.Util;

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

        if (await guildConfigService.GetAsync(e.Guild.Id) == null)
        {
            return;
        }

        logger.LogInformation("{Member} left", e.Member);

        GuildMember member = await db.Find<GuildMember>().OneAsync(e.ToEntityId());

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
            await db.SaveAsync(member);

            // Remove newbie channel
            NewbieChannel newbieChannel = member.Channel;

            if (newbieChannel is not null)
            {
                try
                {
                    DiscordChannel discordChannel = e.Guild.GetChannel(newbieChannel.ChannelId);

                    logger.LogInformation("Removing channel {Channel}", discordChannel);

                    await discordChannel.DeleteAsync($"{e.Member} has been removed");

                    await member.DeleteChannel(db);
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

                    await member.DeleteApplicationWidget(db, sender);

                    await member.DeleteApplication(db);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update status message");
                }
            }
        }).ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception is not null)
                logger.LogError(t.Exception.GetBaseException(), "Unhandled exception in member-removed background task");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}