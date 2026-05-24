using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

using IgorBot.Schema;
using IgorBot.Util;

using static IgorBot.Schema.MemberLifecycleClassifier;

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

        // Capture Discord-side state immediately — roles and join date are unavailable
        // after the member leaves, so log them here before the DB lookup.
        string roles = e.Member.Roles.Any()
            ? string.Join(", ", e.Member.Roles.Select(r => $"{r.Name}({r.Id})"))
            : "(none)";

        logger.LogInformation(
            "{Member} left — Discord roles at removal: [{Roles}], Discord joined {DiscordJoinedAt}",
            e.Member, roles, e.Member.JoinedAt);

        string entityId = $"{e.Guild.Id}-{e.Member.Id}";
        GuildMember? memberOrNull = await db.Find<GuildMember>().OneAsync(entityId);

        if (memberOrNull is null)
        {
            logger.LogWarning("{Member} not found in DB", e.Member);
            return;
        }

        GuildMember member = memberOrNull;

        _ = Task.Run(async () =>
        {
            // Record departure. Only transition to LeftVoluntarily when no more-specific
            // terminal cause (mod kick, ban, auto-kick, honeypot) has already been set.
            // For un-migrated documents (Status == Unknown) the legacy timestamp fields
            // are the source of truth — check them before overwriting with LeftVoluntarily.

            // When the initially-loaded status is still non-terminal, a concurrent handler
            // (AuditLogKickHandler for external kicks, or GuildBanAdded for external bans)
            // may be writing a terminal status to the DB right now. Wait briefly then
            // re-fetch so we see their result before making the classification decision.
            if (IsEligibleForVoluntaryLeave(member))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                member = (await db.Find<GuildMember>().OneAsync(entityId)) ?? member;
            }

            MemberStatusEvent? lastEvent = member.StatusHistory.LastOrDefault();

            logger.LogInformation(
                "Member {Member} departure snapshot — " +
                "status={Status} since {StatusChangedAt} (reason={StatusReason}), " +
                "RemovedByModeration={RemovedByModeration}, " +
                "KickedAt={KickedAt}, BannedAt={BannedAt}, AutoKickedAt={AutoKickedAt}, " +
                "JoinedAt={JoinedAt}, HasApplication={HasApplication}, HasChannel={HasChannel}, " +
                "historyDepth={HistoryDepth}, " +
                "lastTransition=[{LastFrom}->{LastTo} at {LastAt} actor={LastActor} reason={LastReason}]",
                e.Member,
                member.Status, member.StatusChangedAt, member.StatusReason,
                member.RemovedByModeration,
                member.KickedAt, member.BannedAt, member.AutoKickedAt,
                member.JoinedAt, member.Application is not null, member.Channel is not null,
                member.StatusHistory.Count,
                lastEvent?.From, lastEvent?.To, lastEvent?.At, lastEvent?.ActorId, lastEvent?.Reason);

            if (IsEligibleForVoluntaryLeave(member))
            {
                VoluntaryLeavePath path = ClassifyVoluntaryLeavePath(member);
                string reason = path switch
                {
                    VoluntaryLeavePath.LegacyUnknown => "legacy_unknown_voluntary_leave",
                    VoluntaryLeavePath.LegacyDiscoveredBySync => "legacy_discovered_voluntary_leave",
                    _ => "voluntary_leave"
                };

                logger.LogInformation(
                    "Classifying {Member} as voluntary leave (path={Path}, status was {Status})",
                    e.Member, path, member.Status);

                await member.TransitionToAsync(db, MemberStatus.LeftVoluntarily, reason);
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
            NewbieChannel? newbieChannel = member.Channel;

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

            StrangerApplicationEmbed? application = member.Application;

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
