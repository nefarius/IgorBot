using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using IgorBot.Core;
using IgorBot.Schema;
using LiteDB;
using Microsoft.Extensions.Logging;
using Nefarius.DSharpPlus.Extensions.Hosting.Events;

namespace IgorBot.Modules
{
    [DiscordGuildMemberUpdatedEventSubscriber]
    [DiscordGuildMemberRemovedEventSubscriber]
    [DiscordComponentInteractionCreatedEventSubscriber]
    internal class ApplicationWorkflow :
        IDiscordGuildMemberUpdatedEventSubscriber,
        IDiscordGuildMemberRemovedEventSubscriber,
        IDiscordComponentInteractionCreatedEventSubscriber
    {
        private readonly IgorConfig _config;

        private readonly LiteDatabase _db;

        private readonly ILogger<ApplicationWorkflow> _logger;

        public ApplicationWorkflow(ILogger<ApplicationWorkflow> logger, LiteDatabase db, IgorConfig config)
        {
            _logger = logger;
            _db = db;
            _config = config;
        }

        public async Task DiscordOnComponentInteractionCreated(DiscordClient sender,
            ComponentInteractionCreateEventArgs args)
        {
        }

        public async Task DiscordOnGuildMemberRemoved(DiscordClient sender, GuildMemberRemoveEventArgs e)
        {
            if (e.Member.IsBot)
                return;

            if (!_config.Guilds.ContainsKey(e.Guild.Id.ToString()))
                return;

            _logger.LogInformation("{Member} left", e.Member);

            var channelMeta = _db.GetCollection<NewbieChannel>()
                .FindOne(newbieChannel => newbieChannel.DiscordId == e.Member.Id);

            if (channelMeta is null) return;

            var channel = e.Guild.GetChannel(channelMeta.ChannelId);

            if (channel is null)
            {
                _logger.LogWarning("Couldn't find temporary channel to delete");
                throw new InvalidOperationException(
                    $"Channel with ID `{channelMeta.ChannelId}` not found in {e.Guild}");
            }

            _logger.LogInformation("Removing channel {Channel}", channelMeta);

            await channel.DeleteAsync($"{e.Member} has been removed");

            _db.GetCollection<NewbieChannel>().DeleteMany(newbieChannel => newbieChannel.DiscordId == e.Member.Id);
        }

        public async Task DiscordOnGuildMemberUpdated(DiscordClient sender, GuildMemberUpdateEventArgs e)
        {
            if (e.Member.IsBot)
                return;

            if (!_config.Guilds.ContainsKey(e.Guild.Id.ToString()))
                return;

            var guildConfig = _config.Guilds[e.Guild.Id.ToString()];

            var collection = _db.GetCollection<NewbieChannel>();

            //
            // Clean-up on role removal
            // 
            if (e.RolesAfter.All(role => role.Id != guildConfig.StrangerRoleId))
            {
                var channelMeta = _db.GetCollection<NewbieChannel>()
                    .FindOne(newbieChannel => newbieChannel.DiscordId == e.Member.Id);

                if (channelMeta is not null)
                {
                    var chan = e.Guild.GetChannel(channelMeta.ChannelId);

                    if (chan is not null)
                    {
                        _logger.LogInformation("Removing channel {Channel}", channelMeta);

                        await chan.DeleteAsync($"{e.Member} has been removed");
                    }
                }

                _logger.LogInformation("Cleaning up DB entries for {Member}", e.Member);
                collection.DeleteMany(newbieChannel => newbieChannel.DiscordId == e.Member.Id);
                return;
            }

            if (collection.Exists(channel => channel.GuildId == e.Guild.Id && channel.DiscordId == e.Member.Id))
                return;

            var strangerRole = e.Guild.GetRole(guildConfig.StrangerRoleId);

            //
            // Bail without generating log noise
            // 
            if (!e.RolesAfter.Contains(strangerRole))
                return;

            //
            // Member needs to have only the Stranger role
            // 
            if (e.RolesAfter.Count() > 1 || e.RolesAfter.All(r => r != strangerRole))
            {
                _logger.LogWarning("{Member} must have only {Role} assigned, has {Roles}",
                    e.Member, strangerRole, e.RolesAfter);
                return;
            }

            var guildRuntime = _db
                .GetCollection<GuildProperties>()
                .FindOne(properties => properties.GuildId == e.Guild.Id);

            if (guildRuntime is null)
            {
                guildRuntime = new GuildProperties { GuildId = e.Guild.Id };
                _db
                    .GetCollection<GuildProperties>()
                    .Insert(guildRuntime);
            }

            var parentCategory = e.Guild.GetChannel(guildConfig.ApplicationCategoryId);

            _logger.LogInformation("Application category: {Category}", parentCategory);

            var applicationChannelName =
                string.Format(guildConfig.ApplicationChannelNameFormat, guildRuntime.ApplicationChannels);

            _logger.LogInformation("Building overwrites");

            var overwrites = new List<DiscordOverwriteBuilder>();

            var everyoneRole = e.Guild.GetRole(346756263763378176);

            overwrites.Add(new DiscordOverwriteBuilder(everyoneRole).Deny(Permissions.AccessChannels));

            //
            // Add channel permissions for moderators
            // 
            foreach (var moderatorRoleId in guildConfig.ApplicationModeratorRoleIds)
            {
                var role = e.Guild.GetRole(moderatorRoleId);

                if (role is null)
                {
                    _logger.LogWarning("Role with ID {Id} wasn't found in the Discord universe, skipping",
                        moderatorRoleId);
                    continue;
                }

                var overwrite = new DiscordOverwriteBuilder(role);

                overwrite.Allow(Permissions.AccessChannels);
                overwrite.Allow(Permissions.ReadMessageHistory);
                overwrite.Allow(Permissions.ManageMessages);
                overwrite.Allow(Permissions.SendMessages);
                overwrite.Allow(Permissions.EmbedLinks);
                overwrite.Allow(Permissions.AddReactions);

                overwrites.Add(overwrite);
            }

            //
            // Add channel permissions for the affected member
            // 
            var memberOverwrite = new DiscordOverwriteBuilder(e.Member);

            memberOverwrite.Allow(Permissions.AccessChannels);
            memberOverwrite.Allow(Permissions.ReadMessageHistory);
            memberOverwrite.Allow(Permissions.SendMessages);

            overwrites.Add(memberOverwrite);

            _logger.LogInformation("Created {Count} overwrites", overwrites.Count);

            DiscordChannel channel;

            try
            {
                _logger.LogInformation("Attempting to create channel {Channel}", applicationChannelName);

                //
                // Create new text channel private to the member and staff
                // 
                channel = await e.Guild.CreateChannelAsync(
                    applicationChannelName,
                    ChannelType.Text,
                    parentCategory,
                    overwrites: overwrites
                );

                _logger.LogInformation("Created {Channel}", channel);

                //
                // Channel created successfully, increment and save counter
                // 
                guildRuntime.ApplicationChannels++;
                _db.GetCollection<GuildProperties>().Upsert(guildRuntime);

                //
                // Store member to channel association
                // 
                _db.GetCollection<NewbieChannel>().Insert(new NewbieChannel
                {
                    GuildId = e.Guild.Id,
                    DiscordId = e.Member.Id,
                    ChannelId = channel.Id,
                    ChannelName = channel.Name
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Channel creation failed: {Exception}", ex);
                return;
            }

            var welcome = new DiscordMessageBuilder();

            try
            {
                //
                // Get users attention by adding welcome message
                // 
                welcome
                    .WithContent(string.Format(guildConfig.NewbieWelcomeTemplate, e.Member.Mention));

                await channel.SendMessageAsync(welcome);
            }
            catch (Exception ex)
            {
                _logger.LogError("Sending welcome message failed: {Exception}", ex);
            }
        }
    }
}