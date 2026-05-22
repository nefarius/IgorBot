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

        _ = Task.Run(async () =>
        {
            // Record departure. Only transition to LeftVoluntarily if no more-specific
            // terminal status (mod kick, ban, auto-kick, honeypot) has already been set.
            if (member.Status is MemberStatus.Unknown
                || member.Status is MemberStatus.New
                || member.Status is MemberStatus.Onboarding
                || member.Status is MemberStatus.QuestionnaireSubmitted
                || member.Status is MemberStatus.FullMember
                || member.Status is MemberStatus.StrangerRoleRemoved)
            {
                await member.TransitionToAsync(db, MemberStatus.LeftVoluntarily);
            }
            else
            {
                // Terminal state was already set (e.g. by honeypot, KickStaleInvokable, or a
                // panel action that fired before the Discord event arrived). Just stamp LeftAt
                // for legacy queries without overwriting the canonical status.
                member.LeftAt = DateTime.UtcNow;
                await db.SaveAsync(member);
            }

            // Remove newbie channel (only strangers in onboarding have one)
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

                        // Refresh the widget so buttons are removed and final state is shown;
                        // do NOT delete it — mods need to see the panel after the action.
                        await member.UpdateApplicationWidget(sender);
                        return;
                    }

                    logger.LogInformation(
                        member.AutoKickedAt.HasValue
                            ? "{Member} removed due to idle timeout"
                            : "{Member} left by themselves", e.Member);

                    await member.DeleteApplicationWidget(db, sender);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update status message");
                }
            }
        }).ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception is not null)
            {
                logger.LogError(t.Exception.GetBaseException(),
                    "Unhandled exception in member-removed background task");
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
