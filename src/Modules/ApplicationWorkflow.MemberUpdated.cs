using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

using IgorBot.Core;
using IgorBot.Handlers;
using IgorBot.Schema;
using IgorBot.Util;

using MongoDB.Entities;

namespace IgorBot.Modules;

internal partial class ApplicationWorkflow
{
    //
    // Called when a member was modified
    //
    public async Task DiscordOnGuildMemberUpdated(DiscordClient sender, GuildMemberUpdateEventArgs e)
    {
        if (e.Member.IsBot)
        {
            return;
        }

        if (!_config.Value.Guilds.ContainsKey(e.Guild.Id.ToString()))
        {
            return;
        }

        // At this point we expect to have the user in the DB
        GuildMember member = await DB.Find<GuildMember>().OneAsync(e.ToEntityId());

        if (member is null)
        {
            _logger.LogWarning("{Member} not found in DB", e.Member);
            return;
        }

        GuildConfig guildConfig = _config.Value.Guilds[e.Guild.Id.ToString()];

        DiscordChannel strangerStatusChannel = e.Guild.GetChannel(guildConfig.StrangerStatusChannelId);

        if (strangerStatusChannel is null)
        {
            throw new InvalidOperationException("Failed to get stranger status channel.");
        }

        _ = Task.Run(async () =>
        {
            // Full member role added
            if (e.RolesBefore.All(role => role.Id != guildConfig.MemberRoleId) &&
                e.RolesAfter.Any(role => role.Id == guildConfig.MemberRoleId))
            {
                _logger.LogInformation("Full member role set for {Member}", e.Member);
                member.FullMemberAt = DateTime.UtcNow;
                await member.SaveAsync();
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

            DiscordRole strangerRole = e.Guild.GetRole(guildConfig.StrangerRoleId);

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
                _logger.LogWarning("{Member} must have only {Role} assigned, has {Roles}",
                    e.Member, strangerRole, e.RolesAfter);
                return;
            }

            await ProcessStrangerAssignment(e, guildConfig, member);
        });
    }

    /// <summary>
    ///     Tasks required when a member got the Stranger role assigned.
    /// </summary>
    private async Task ProcessStrangerAssignment(
        GuildMemberUpdateEventArgs e,
        GuildConfig guildConfig,
        GuildMember member
    )
    {
        GuildProperties guildRuntime = await DB.Find<GuildProperties>().OneAsync(e.Guild.Id.ToString());

        if (guildRuntime is null)
        {
            guildRuntime = new GuildProperties { GuildId = e.Guild.Id };
            await guildRuntime.SaveAsync();
        }

        NewMemberMessage message = new()
        {
            GuildProperties = guildRuntime, GuildConfig = guildConfig, MemberEntiryId = member.ID
        };

        _logger.LogInformation("Submitting new member workflow message");

        await _messageBus.SendLocal(message);
    }

    /// <summary>
    ///     Tasks required when a member got the Stranger role removed.
    /// </summary>
    private async Task ProcessStrangerRoleRemoved(
        DiscordClient client,
        GuildMemberUpdateEventArgs e,
        GuildMember member
    )
    {
        _logger.LogInformation("Stranger role removed for {Member}", member);

        member.StrangerRoleRemovedAt = DateTime.UtcNow;
        member.IsOnboardingInProgress = false;
        await member.SaveAsync();

        // Remove channel
        NewbieChannel newbieChannel = member.Channel;

        if (newbieChannel is not null)
        {
            DiscordChannel discordChannel = e.Guild.GetChannel(newbieChannel.ChannelId);

            if (discordChannel is not null)
            {
                _logger.LogInformation("Removing channel {Channel}", discordChannel);

                await discordChannel.DeleteAsync($"{e.Member} is no longer a stranger");

                await member.DeleteChannel();
            }
        }

        if (member.Application is not null)
        {
            //
            // Update status message
            // 

            try
            {
                _logger.LogInformation("Removing application widget for {Member}", member);

                await member.DeleteApplicationWidget(client);
            }
            catch (NotFoundException ex)
            {
                _logger.LogError(ex, "Couldn't find status message");
            }
        }
    }
}