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
            // Record departure. Only transition to LeftVoluntarily when no more-specific
            // terminal cause (mod kick, ban, auto-kick, honeypot) has already been set.
            // For un-migrated documents (Status == Unknown) the legacy timestamp fields
            // are the source of truth — check them before overwriting with LeftVoluntarily.
            logger.LogInformation(
                "Member {Member} departure: loaded status {Status}, RemovedByModeration={RemovedByModeration}, " +
                "KickedAt={KickedAt}, BannedAt={BannedAt}, AutoKickedAt={AutoKickedAt}, history depth {HistoryDepth}",
                e.Member, member.Status, member.RemovedByModeration,
                member.KickedAt, member.BannedAt, member.AutoKickedAt, member.StatusHistory.Count);

            if (IsEligibleForVoluntaryLeave(member))
            {
                logger.LogInformation("Classifying {Member} as voluntary leave (status was {Status})",
                    e.Member, member.Status);
                await member.TransitionToAsync(db, MemberStatus.LeftVoluntarily);
            }
            else
            {
                // Terminal state was already set (e.g. by honeypot, KickStaleInvokable, or a
                // panel action that fired before the Discord event arrived). Just stamp LeftAt
                // for legacy queries without overwriting the canonical status.
                logger.LogInformation(
                    "Preserving terminal status {Status} for {Member}, stamping LeftAt only",
                    member.Status, e.Member);
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

    /// <summary>
    ///     Returns true when a member's departure should be recorded as a voluntary leave.
    ///     Migrated documents (Status != Unknown) are eligible when in a non-terminal state.
    ///     Un-migrated documents (Status == Unknown) are eligible only when no legacy terminal
    ///     timestamp (KickedAt, BannedAt, AutoKickedAt) has already been set.
    /// </summary>
    private static bool IsEligibleForVoluntaryLeave(GuildMember member) =>
        member.Status switch
        {
            MemberStatus.New or
            MemberStatus.Onboarding or
            MemberStatus.QuestionnaireSubmitted or
            MemberStatus.FullMember or
            MemberStatus.StrangerRoleRemoved => true,

            // Legacy document: defer to timestamp fields to avoid overwriting a terminal marker.
            MemberStatus.Unknown =>
                !member.KickedAt.HasValue &&
                !member.BannedAt.HasValue &&
                !member.AutoKickedAt.HasValue,

            // Already in a terminal state set by a prior action — do not overwrite.
            _ => false
        };
}
