using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Util;

namespace IgorBot.Modules;

internal partial class ApplicationWorkflow
{
    //
    // Called when a member was modified
    //
    public async Task HandleEventAsync(DiscordClient sender, GuildMemberUpdatedEventArgs e)
    {
        if (e.Member.IsBot)
        {
            return;
        }

        GuildConfig? guildConfig = await guildConfigService.GetAsync(e.Guild.Id);
        if (guildConfig == null)
        {
            return;
        }

        // At this point we expect to have the user in the DB
        GuildMember? memberOrNull = await db.Find<GuildMember>().OneAsync(e.ToEntityId());

        if (memberOrNull is null)
        {
            logger.LogWarning("{Member} not found in DB", e.Member);
            return;
        }

        GuildMember member = memberOrNull;

        ObservableBackgroundWork.Run(async cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Full member role added
            if (e.RolesBefore.All(role => role.Id != guildConfig.MemberRoleId) &&
                e.RolesAfter.Any(role => role.Id == guildConfig.MemberRoleId))
            {
                logger.LogInformation("Full member role set for {Member}", e.Member);
                // Conditional: already-promoted or departed members are left alone.
                await member.TryTransitionAsync(db, MemberStatus.FullMember);
                return;
            }

            // Stranger role was removed
            if (e.RolesBefore.Any(role => role.Id == guildConfig.StrangerRoleId) &&
                e.RolesAfter.All(role => role.Id != guildConfig.StrangerRoleId))
            {
                await ProcessStrangerRoleRemoved(sender, e, member);

                return;
            }

            // Required resources exist already
            if (member.Channel is not null && member.Application is not null)
            {
                return;
            }

            if (!e.Guild.Roles.TryGetValue(guildConfig.StrangerRoleId, out DiscordRole? strangerRole))
            {
                return;
            }

            //
            // Bail without generating log noise
            // 
            if (!e.RolesAfter.Contains(strangerRole))
            {
                return;
            }

            //
            // Member needs to have only the Stranger role
            // 
            if (e.RolesAfter.Count > 1 || e.RolesAfter.All(r => r != strangerRole))
            {
                logger.LogWarning("{Member} must have only {Role} assigned, has {Roles}",
                    e.Member, strangerRole, e.RolesAfter);
                return;
            }

            if (!guildConfig.EnableOnboardingWorkflow)
            {
                return;
            }

            if (guildConfig.StrangerStatusChannelId == 0)
            {
                logger.LogDebug("Guild {GuildId} has no stranger status channel configured, skipping ProcessStrangerAssignment",
                    e.Guild.Id);
                return;
            }

            if (e.Guild.FindChannel(guildConfig.StrangerStatusChannelId) is null)
            {
                logger.LogWarning("Stranger status channel not found for guild {GuildId}, skipping ProcessStrangerAssignment",
                    e.Guild.Id);
                return;
            }

            await ProcessStrangerAssignment(e.Guild, guildConfig, member);
        }, lifetime.ApplicationStopping, logger, "member-updated");
    }

    /// <summary>
    ///     Tasks required when a member got the Stranger role assigned.
    /// </summary>
    private Task ProcessStrangerAssignment(DiscordGuild guild, GuildConfig guildConfig, GuildMember member)
    {
        return onboardingSubmitter.SubmitAsync(guild, guildConfig, member);
    }

    /// <summary>
    ///     Tasks required when a member got the Stranger role removed.
    /// </summary>
    private async Task ProcessStrangerRoleRemoved(
        DiscordClient client,
        GuildMemberUpdatedEventArgs e,
        GuildMember member
    )
    {
        logger.LogInformation("Stranger role removed for {Member}", member);

        // Conditional: a concurrent promotion (FullMember) or departure wins. The cleanup below still runs
        // either way, because the channel and widget are obsolete in all of those cases.
        // The in-progress flag is owned by NewMemberHandler and is deliberately not touched here.
        if (!await member.TryTransitionAsync(db, MemberStatus.StrangerRoleRemoved))
        {
            logger.LogInformation(
                "StrangerRoleRemoved for {Member} not recorded: persisted status already moved on", member);
        }

        // Remove channel
        NewbieChannel? newbieChannel = member.Channel;

        if (newbieChannel is not null)
        {
            DiscordChannel? discordChannel = e.Guild.FindChannel(newbieChannel.ChannelId);

            try
            {
                if (discordChannel is not null)
                {
                    logger.LogInformation("Removing channel {Channel}", discordChannel);

                    try
                    {
                        await discordChannel.DeleteAsync($"{e.Member} is no longer a stranger");
                    }
                    catch (NotFoundException)
                    {
                        logger.LogWarning("Channel {ChannelId} was already deleted", newbieChannel.ChannelId);
                    }
                }

                // Deleted, already gone or not in the cache: the DB reference is obsolete.
                await member.DeleteChannel(db);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to remove newbie channel for {Member}", member);
            }
        }

        if (member.Application is not null)
        {
            //
            // Update status message
            // 

            try
            {
                logger.LogInformation("Removing application widget for {Member}", member);

                await member.DeleteApplicationWidget(db, client);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Couldn't remove application widget for {Member}", member);
            }
        }
    }
}