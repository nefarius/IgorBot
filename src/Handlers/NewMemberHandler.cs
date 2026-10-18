using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;
using IgorBot.Util;

using MongoDB.Driver;
using MongoDB.Entities;

namespace IgorBot.Handlers;

/// <summary>
///     Handles new stranger appeared workflow.
/// </summary>
internal sealed class NewMemberHandler(
    DB db,
    DiscordClient client,
    IGuildConfigService guildConfigService,
    ILogger<NewMemberHandler> logger)
{
    public async Task ProcessAsync(NewMemberMessage message)
    {
        logger.LogInformation("Processing new member workflow");

        GuildMember? dbMember = await db.Find<GuildMember>().OneAsync(message.MemberEntryId);

        if (dbMember is null)
        {
            logger.LogWarning("GuildMember {MemberEntryId} not found in DB, skipping stale queue entry",
                message.MemberEntryId);
            return;
        }

        logger.LogDebug("Got member from DB: {@Member}", dbMember);

        // A record with only one of channel/widget is a partial setup; only the missing artifact is recreated.
        if ((dbMember.Channel is not null && dbMember.Application is not null) || dbMember.IsOnboardingInProgress)
        {
            logger.LogWarning("Member {Member} already has an active newbie channel and widget, aborting", dbMember);
            return;
        }

        if (!await TryClaimOnboardingAsync(dbMember))
        {
            logger.LogWarning("Member {Member} is already being onboarded elsewhere, aborting", dbMember);
            return;
        }

        try
        {
            // The queued config can be stale when many members are submitted at once (e.g. reconciliation)
            // or settings changed while queued, so prefer the current one.
            GuildConfig guildConfig = await guildConfigService.GetAsync(message.GuildConfig.GuildId) ??
                                      message.GuildConfig;

            if (guildConfig.StrangerStatusChannelId == 0 || guildConfig.ApplicationCategoryId == 0)
            {
                logger.LogWarning("Guild {GuildId} has invalid onboarding config (missing StrangerStatusChannelId or ApplicationCategoryId), skipping",
                    guildConfig.GuildId);
                return;
            }

            if (string.IsNullOrEmpty(guildConfig.ApplicationChannelNameFormat) ||
                string.IsNullOrEmpty(guildConfig.NewbieWelcomeTemplate))
            {
                logger.LogWarning("Guild {GuildId} has invalid onboarding config (missing channel format or welcome template), skipping",
                    guildConfig.GuildId);
                return;
            }

            DiscordGuild guild = client.Guilds[guildConfig.GuildId];
            DiscordMember guildMember = await guild.GetMemberAsync(dbMember.MemberId);
            DiscordChannel? strangerStatusChannel = guild.FindChannel(guildConfig.StrangerStatusChannelId);
            DiscordChannel? parentCategory = guild.FindChannel(guildConfig.ApplicationCategoryId);

            if (strangerStatusChannel is null || parentCategory is null)
            {
                logger.LogWarning("Guild {GuildId} stranger status channel or application category not found, skipping",
                    guildConfig.GuildId);
                return;
            }

            logger.LogInformation("Application category: {Category}", parentCategory);

            logger.LogDebug("Building overwrites");

            List<DiscordOverwriteBuilder> overwrites = new();

            DiscordRole everyoneRole = guild.EveryoneRole;

            overwrites.Add(new DiscordOverwriteBuilder(everyoneRole)
                .Deny(NewbieChannelAccessPolicy.ToPermissions(NewbieChannelAccessPolicy.EveryoneDenied)));

            //
            // Add channel permissions for moderators
            // 
            bool resolvedModeratorRole = false;
            foreach (ulong moderatorRoleId in guildConfig.ApplicationModeratorRoleIds)
            {
                if (!guild.Roles.TryGetValue(moderatorRoleId, out DiscordRole? role))
                {
                    logger.LogWarning("Role with ID {Id} wasn't found in the Discord universe, skipping",
                        moderatorRoleId);
                    continue;
                }

                resolvedModeratorRole = true;
                DiscordOverwriteBuilder overwrite = new(role);
                overwrite.Allow(NewbieChannelAccessPolicy.ToPermissions(NewbieChannelAccessPolicy.ModeratorAllowed));
                overwrites.Add(overwrite);
            }

            if (guildConfig.ApplicationModeratorRoleIds.Count > 0 && !resolvedModeratorRole)
            {
                logger.LogWarning(
                    "Guild {GuildId} has no resolvable moderator role, skipping channel creation",
                    guildConfig.GuildId);
                return;
            }

            //
            // Add channel permissions for the affected member
            // 
            DiscordOverwriteBuilder memberOverwrite = new(guildMember);
            memberOverwrite.Allow(NewbieChannelAccessPolicy.ToPermissions(NewbieChannelAccessPolicy.MemberAllowed));

            overwrites.Add(memberOverwrite);

            logger.LogDebug("Created {Count} overwrites", overwrites.Count);

            DiscordChannel? channel = null;
            bool createdChannel = false;

            if (dbMember.Channel is null)
            {

                try
                {
                    // Reserve the number atomically so queued members can never share a channel name.
                    ulong channelNumber = await ReserveChannelNumberAsync(guildConfig.GuildId);
                    string applicationChannelName =
                        string.Format(guildConfig.ApplicationChannelNameFormat, channelNumber);

                    logger.LogDebug("Attempting to create channel {Channel}", applicationChannelName);

                    //
                    // Create new text channel private to the member and staff
                    // 
                    channel = await guild.CreateChannelAsync(
                        applicationChannelName,
                        DiscordChannelType.Text,
                        parentCategory,
                        overwrites: overwrites
                    );

                    logger.LogInformation("Created {Channel}", channel);

                    try
                    {
                        //
                        // Store member to channel association
                        // 
                        await dbMember.CreateNewbieChannel(db, guild, channel);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to persist channel creation, rolling back");
                        try
                        {
                            await channel.DeleteAsync();
                        }
                        catch (Exception deleteEx)
                        {
                            logger.LogError(deleteEx, "Failed to delete orphan channel {ChannelId}", channel.Id);
                        }

                        return;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Channel creation failed");
                    return;
                }

                createdChannel = true;
            }

            if (createdChannel && channel is not null)
            {
                try
                {
                    //
                    // Get users attention by adding the welcome message
                    //
                    await channel.SendMessageAsync(new DiscordMessageBuilder()
                        .WithContent(string.Format(guildConfig.NewbieWelcomeTemplate, guildMember.Mention)));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Sending welcome message failed, rolling back channel creation");
                    await RollbackChannelCreationAsync(dbMember, channel);
                    return;
                }
            }

            bool createdWidget = false;

            if (dbMember.Application is null)
            {
                try
                {
                    await dbMember.CreateApplicationWidget(db, client, guild, strangerStatusChannel);
                    createdWidget = true;
                }
                catch (Exception ex)
                {
                    if (createdChannel && channel is not null)
                    {
                        logger.LogError(ex, "Creating status widget failed, rolling back channel creation");
                        await RollbackChannelCreationAsync(dbMember, channel);
                    }
                    else
                    {
                        // Existing channel is preserved; the next reconciliation retries the widget.
                        logger.LogError(ex, "Creating status widget failed for {Member}", dbMember);
                    }

                    return;
                }
            }
            else if (createdChannel)
            {
                // Existing widget predates the new channel, refresh it so it links to it.
                try
                {
                    await dbMember.UpdateApplicationWidget(client);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to refresh existing widget for {Member}", dbMember);
                }
            }

            if (dbMember.Status is MemberStatus.Onboarding or MemberStatus.QuestionnaireSubmitted)
            {
                return;
            }

            try
            {
                await dbMember.TransitionToAsync(db, MemberStatus.Onboarding);
            }
            catch (Exception ex)
            {
                if (!createdChannel && !createdWidget)
                {
                    logger.LogError(ex, "TransitionToAsync failed for {Member}", dbMember);
                    return;
                }

                // Clean up only what this run created so no orphaned artifact is left behind.
                logger.LogError(ex, "TransitionToAsync failed after setup, rolling back created artifacts");
                if (createdWidget)
                {
                    try
                    {
                        await dbMember.RemoveApplicationWidget(db, client);
                    }
                    catch (Exception widgetEx)
                    {
                        logger.LogError(widgetEx,
                            "Failed to delete orphan application widget during rollback");
                    }
                }

                if (createdChannel && channel is not null)
                {
                    await RollbackChannelCreationAsync(dbMember, channel);
                }
            }
        }
        finally
        {
            await SetOnboardingInProgressAsync(dbMember, false);
        }
    }

    /// <summary>
    ///     Atomically sets the in-progress flag only if nobody else holds it.
    /// </summary>
    private async Task<bool> TryClaimOnboardingAsync(GuildMember member)
    {
        UpdateResult result = await db.Update<GuildMember>()
            .Match(m => m.Eq(f => f.ID, member.ID) & m.Ne(f => f.IsOnboardingInProgress, true))
            .Modify(m => m.IsOnboardingInProgress, true)
            .ExecuteAsync();

        if (result.ModifiedCount != 1)
        {
            return false;
        }

        member.IsOnboardingInProgress = true;
        return true;
    }

    /// <summary>
    ///     Targeted update so a long-running onboarding never overwrites concurrent changes to the member document.
    /// </summary>
    private async Task SetOnboardingInProgressAsync(GuildMember member, bool value)
    {
        member.IsOnboardingInProgress = value;
        await db.Update<GuildMember>()
            .MatchID(member.ID)
            .Modify(m => m.IsOnboardingInProgress, value)
            .ExecuteAsync();
    }

    /// <summary>
    ///     Atomically takes the next channel number. Numbers are never handed back, so rollbacks leave gaps
    ///     instead of risking duplicates.
    /// </summary>
    private async Task<ulong> ReserveChannelNumberAsync(ulong guildId)
    {
        string id = guildId.ToString();
        GuildProperties? before = await db.Collection<GuildProperties>().FindOneAndUpdateAsync(
            Builders<GuildProperties>.Filter.Eq(g => g.ID, id),
            Builders<GuildProperties>.Update.Inc(g => g.ApplicationChannels, 1UL),
            new FindOneAndUpdateOptions<GuildProperties> { ReturnDocument = ReturnDocument.Before });

        if (before is not null)
        {
            return before.ApplicationChannels;
        }

        // First use for this guild: create the document, then reserve again.
        await db.SaveAsync(new GuildProperties { GuildId = guildId });
        return await ReserveChannelNumberAsync(guildId);
    }

    private async Task RollbackChannelCreationAsync(GuildMember dbMember, DiscordChannel channel)
    {
        try
        {
            try
            {
                await channel.DeleteAsync();
            }
            catch (Exception deleteEx)
            {
                logger.LogError(deleteEx, "Failed to delete orphan channel {ChannelId} during rollback", channel.Id);
            }

            await dbMember.DeleteChannel(db);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete channel during rollback");
            throw;
        }
    }
}