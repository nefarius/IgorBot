﻿using System.Diagnostics.CodeAnalysis;

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

    public string MemberEntiryId { get; init; }
}

/// <summary>
///     Handles new stranger appeared workflow.
/// </summary>
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal sealed class NewMemberHandler : IHandleMessages<NewMemberMessage>
{
    private readonly IDiscordClientService _clientService;
    private readonly ILogger<NewMemberHandler> _logger;

    public NewMemberHandler(IDiscordClientService clientService, ILogger<NewMemberHandler> logger)
    {
        _clientService = clientService;
        _logger = logger;
    }

    public async Task Handle(NewMemberMessage message)
    {
        _logger.LogInformation("Processing new member workflow");

        GuildMember dbMember = await DB.Find<GuildMember>().OneAsync(message.MemberEntiryId);

        _logger.LogDebug("Got member from DB: {@Member}", dbMember);

        if (dbMember.Channel is not null || dbMember.IsOnboardingInProgress)
        {
            _logger.LogWarning("Member {Member} already has an active newbie channel, aborting", dbMember);
            return;
        }

        dbMember.IsOnboardingInProgress = true;
        await dbMember.SaveAsync();

        try
        {
            GuildProperties guildProperties = message.GuildProperties;
            DiscordClient client = _clientService.Client;
            GuildConfig guildConfig = message.GuildConfig;

            DiscordGuild guild = client.Guilds[guildConfig.GuildId];
            DiscordMember guildMember = await guild.GetMemberAsync(dbMember.MemberId);
            DiscordChannel strangerStatusChannel = await guild.GetChannelAsync(guildConfig.StrangerStatusChannelId);

            DiscordChannel parentCategory = await guild.GetChannelAsync(guildConfig.ApplicationCategoryId);

            _logger.LogInformation("Application category: {Category}", parentCategory);

            string applicationChannelName =
                string.Format(guildConfig.ApplicationChannelNameFormat, guildProperties.ApplicationChannels);

            _logger.LogDebug("Building overwrites");

            List<DiscordOverwriteBuilder> overwrites = new();

            DiscordRole everyoneRole = guild.EveryoneRole;

            overwrites.Add(new DiscordOverwriteBuilder(everyoneRole).Deny(DiscordPermissions.AccessChannels));

            //
            // Add channel permissions for moderators
            // 
            foreach (ulong moderatorRoleId in guildConfig.ApplicationModeratorRoleIds)
            {
                try
                {
                    DiscordRole role = guild.GetRole(moderatorRoleId);
                    DiscordOverwriteBuilder overwrite = new(role);

                    overwrite.Allow(DiscordPermissions.AccessChannels);
                    overwrite.Allow(DiscordPermissions.ReadMessageHistory);
                    overwrite.Allow(DiscordPermissions.ManageMessages);
                    overwrite.Allow(DiscordPermissions.SendMessages);
                    overwrite.Allow(DiscordPermissions.EmbedLinks);
                    overwrite.Allow(DiscordPermissions.AddReactions);

                    overwrites.Add(overwrite);
                }
                catch (ServerErrorException)
                {
                    _logger.LogWarning("Role with ID {Id} wasn't found in the Discord universe, skipping",
                        moderatorRoleId);
                }
            }

            //
            // Add channel permissions for the affected member
            // 
            DiscordOverwriteBuilder memberOverwrite = new(guildMember);

            memberOverwrite.Allow(DiscordPermissions.AccessChannels);
            memberOverwrite.Allow(DiscordPermissions.ReadMessageHistory);
            memberOverwrite.Allow(DiscordPermissions.SendMessages);
            memberOverwrite.Allow(DiscordPermissions.AttachFiles);
            memberOverwrite.Allow(DiscordPermissions.EmbedLinks);
            memberOverwrite.Allow(DiscordPermissions.AddReactions);

            overwrites.Add(memberOverwrite);

            _logger.LogDebug("Created {Count} overwrites", overwrites.Count);

            DiscordChannel channel;

            try
            {
                _logger.LogDebug("Attempting to create channel {Channel}", applicationChannelName);

                //
                // Create new text channel private to the member and staff
                // 
                channel = await guild.CreateChannelAsync(
                    applicationChannelName,
                    DiscordChannelType.Text,
                    parentCategory,
                    overwrites: overwrites
                );

                _logger.LogInformation("Created {Channel}", channel);

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
                _logger.LogError(ex, "Channel creation failed");
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
                _logger.LogError(ex, "Sending welcome message failed");
            }

            try
            {
                await dbMember.CreateApplicationWidget(client, guild, strangerStatusChannel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Creating status message failed");
            }
        }
        finally
        {
            dbMember.IsOnboardingInProgress = false;
            await dbMember.SaveAsync();
        }
    }
}