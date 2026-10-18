using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Util;

using MongoDB.Entities;

namespace IgorBot.Handlers;

/// <summary>
///     Handles new stranger appeared workflow.
/// </summary>
internal sealed class NewMemberHandler(
    DB db,
    DiscordClient client,
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

        dbMember.IsOnboardingInProgress = true;
        await db.SaveAsync(dbMember);

        try
        {
            GuildProperties guildProperties = message.GuildProperties;
            GuildConfig guildConfig = message.GuildConfig;

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

            string applicationChannelName =
                string.Format(guildConfig.ApplicationChannelNameFormat, guildProperties.ApplicationChannels);

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
                        // Channel created successfully, increment and save counter
                        // 
                        guildProperties.ApplicationChannels++;
                        await db.SaveAsync(guildProperties);

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

                        guildProperties.ApplicationChannels--;
                        try
                        {
                            await db.SaveAsync(guildProperties);
                        }
                        catch (Exception rollbackEx)
                        {
                            logger.LogError(rollbackEx, "Failed to roll back ApplicationChannels counter");
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
                    await RollbackChannelCreationAsync(dbMember, channel, guildProperties);
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
                        await RollbackChannelCreationAsync(dbMember, channel, guildProperties);
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
                        await dbMember.DeleteApplicationWidget(db, client);
                    }
                    catch (Exception widgetEx)
                    {
                        logger.LogError(widgetEx,
                            "Failed to delete orphan application widget during rollback");
                    }
                }

                if (createdChannel && channel is not null)
                {
                    await RollbackChannelCreationAsync(dbMember, channel, guildProperties);
                }
            }
        }
        finally
        {
            dbMember.IsOnboardingInProgress = false;
            await db.SaveAsync(dbMember);
        }
    }

    private async Task RollbackChannelCreationAsync(GuildMember dbMember, DiscordChannel channel,
        GuildProperties guildProperties)
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
        finally
        {
            guildProperties.ApplicationChannels--;
            try
            {
                await db.SaveAsync(guildProperties);
            }
            catch (Exception rollbackEx)
            {
                logger.LogError(rollbackEx, "Failed to roll back ApplicationChannels counter");
            }
        }
    }
}