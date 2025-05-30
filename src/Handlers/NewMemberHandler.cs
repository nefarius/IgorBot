using System.Diagnostics.CodeAnalysis;

using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

using IgorBot.Core;
using IgorBot.Schema;

using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting;

using Rebus.Handlers;

namespace IgorBot.Handlers;

internal sealed class NewMemberMessage
{
    public GuildProperties GuildProperties { get; init; }

    public GuildConfig GuildConfig { get; init; }

    public string MemberEntryId { get; init; }
}

/// <summary>
///     Handles new stranger appeared workflow.
/// </summary>
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal sealed class NewMemberHandler(IDiscordClientService clientService, ILogger<NewMemberHandler> logger)
    : IHandleMessages<NewMemberMessage>
{
    public async Task Handle(NewMemberMessage message)
    {
        logger.LogInformation("Processing new member workflow");

        GuildMember dbMember = await DB.Find<GuildMember>().OneAsync(message.MemberEntryId);

        logger.LogDebug("Got member from DB: {@Member}", dbMember);

        if (dbMember.Channel is not null || dbMember.IsOnboardingInProgress)
        {
            logger.LogWarning("Member {Member} already has an active newbie channel, aborting", dbMember);
            return;
        }

        dbMember.IsOnboardingInProgress = true;
        await dbMember.SaveAsync();

        try
        {
            GuildProperties guildProperties = message.GuildProperties;
            DiscordClient client = clientService.Client;
            GuildConfig guildConfig = message.GuildConfig;

            DiscordGuild guild = client.Guilds[guildConfig.GuildId];
            DiscordMember guildMember = await guild.GetMemberAsync(dbMember.MemberId);
            DiscordChannel strangerStatusChannel = guild.GetChannel(guildConfig.StrangerStatusChannelId);

            DiscordChannel parentCategory = guild.GetChannel(guildConfig.ApplicationCategoryId);

            logger.LogInformation("Application category: {Category}", parentCategory);

            string applicationChannelName =
                string.Format(guildConfig.ApplicationChannelNameFormat, guildProperties.ApplicationChannels);

            logger.LogDebug("Building overwrites");

            List<DiscordOverwriteBuilder> overwrites = new();

            DiscordRole everyoneRole = guild.EveryoneRole;

            overwrites.Add(new DiscordOverwriteBuilder(everyoneRole).Deny(Permissions.AccessChannels));

            //
            // Add channel permissions for moderators
            // 
            foreach (ulong moderatorRoleId in guildConfig.ApplicationModeratorRoleIds)
            {
                try
                {
                    DiscordRole role = guild.GetRole(moderatorRoleId);
                    DiscordOverwriteBuilder overwrite = new(role);

                    overwrite.Allow(Permissions.AccessChannels);
                    overwrite.Allow(Permissions.ReadMessageHistory);
                    overwrite.Allow(Permissions.ManageMessages);
                    overwrite.Allow(Permissions.SendMessages);
                    overwrite.Allow(Permissions.EmbedLinks);
                    overwrite.Allow(Permissions.AddReactions);

                    overwrites.Add(overwrite);
                }
                catch (ServerErrorException)
                {
                    logger.LogWarning("Role with ID {Id} wasn't found in the Discord universe, skipping",
                        moderatorRoleId);
                }
            }

            //
            // Add channel permissions for the affected member
            // 
            DiscordOverwriteBuilder memberOverwrite = new(guildMember);

            memberOverwrite.Allow(Permissions.AccessChannels);
            memberOverwrite.Allow(Permissions.ReadMessageHistory);
            memberOverwrite.Allow(Permissions.SendMessages);
            memberOverwrite.Allow(Permissions.AttachFiles);
            memberOverwrite.Allow(Permissions.EmbedLinks);
            memberOverwrite.Allow(Permissions.AddReactions);

            overwrites.Add(memberOverwrite);

            logger.LogDebug("Created {Count} overwrites", overwrites.Count);

            DiscordChannel channel;

            try
            {
                logger.LogDebug("Attempting to create channel {Channel}", applicationChannelName);

                //
                // Create new text channel private to the member and staff
                // 
                channel = await guild.CreateChannelAsync(
                    applicationChannelName,
                    ChannelType.Text,
                    parentCategory,
                    overwrites: overwrites
                );

                logger.LogInformation("Created {Channel}", channel);

                //
                // Channel created successfully, increment and save counter
                // 
                guildProperties.ApplicationChannels++;
                await guildProperties.SaveAsync();

                //
                // Store member to channel association
                // 
                await dbMember.CreateNewbieChannel(guild, channel);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Channel creation failed");
                return;
            }

            try
            {
                //
                // Get users attention by adding welcome message
                // 

                await channel.SendMessageAsync(new DiscordMessageBuilder()
                    .WithContent(string.Format(guildConfig.NewbieWelcomeTemplate, guildMember.Mention)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sending welcome message failed");
            }

            try
            {
                await dbMember.CreateApplicationWidget(client, guild, strangerStatusChannel);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Creating status message failed");
            }
        }
        finally
        {
            dbMember.IsOnboardingInProgress = false;
            await dbMember.SaveAsync();
        }
    }
}