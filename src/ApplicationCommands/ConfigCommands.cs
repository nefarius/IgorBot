using System.Diagnostics.CodeAnalysis;

using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;

using IgorBot.Core;
using IgorBot.Services;

namespace IgorBot.ApplicationCommands;

[SlashCommandGroup("config", "Configure the bot for this server. Administrator only.")]
[SlashRequirePermissions(Permissions.Administrator)]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public sealed class ConfigCommands(IGuildConfigService guildConfigService) : ApplicationCommandModule
{
    [SlashCommand("setup", "Initial setup or overwrite configuration for this server.")]
    public async Task Setup(
        InteractionContext ctx,
        [Option("stranger_role", "Role assigned to new members before they complete onboarding")]
        DiscordRole strangerRole,
        [Option("member_role", "Role assigned when a member is promoted")]
        DiscordRole memberRole,
        [Option("application_category", "Category where newbie channels are created")]
        DiscordChannel applicationCategory,
        [Option("stranger_status_channel", "Channel where application status embeds appear")]
        DiscordChannel strangerStatusChannel,
        [Option("member_welcome_channel", "Channel where welcome messages for promoted members appear")]
        DiscordChannel memberWelcomeChannel,
        [Option("application_channel_format", "Format for newbie channel names, use {0} for number")]
        string applicationChannelFormat = "newbie-{0:D4}",
        [Option("newbie_welcome_template", "Welcome message template, use {0} for member mention")]
        string newbieWelcomeTemplate =
            "Welcome, {0}! Before you can become a full member, we wanna know a bit about you. Please enter **/apply member** to start!",
        [Option("member_welcome_template", "Welcome message for promoted members, use {0} for member mention")]
        string memberWelcomeTemplate = "Welcome {0}, enjoy your stay!",
        [Option("auto_assign_stranger_role", "Automatically assign stranger role when member joins")]
        bool autoAssignStrangerRole = false,
        [Option("idle_kick_minutes", "Minutes before kicking inactive strangers (0 or omit to disable)")]
        long idleKickMinutes = 0,
        [Option("honeypot_channel", "Channel that bans users who post in it (optional)")]
        DiscordChannel honeypotChannel = null,
        [Option("moderator_role", "Role that can see and interact with newbie channels (optional)")]
        DiscordRole moderatorRole = null,
        [Option("enable_onboarding_workflow", "Run onboarding workflow when member gets stranger role")]
        bool enableOnboardingWorkflow = true
    )
    {
        await ctx.DeferAsync();

        GuildConfig config = new()
        {
            GuildId = ctx.Guild.Id,
            StrangerRoleId = strangerRole.Id,
            MemberRoleId = memberRole.Id,
            ApplicationCategoryId = applicationCategory.Id,
            StrangerStatusChannelId = strangerStatusChannel.Id,
            MemberWelcomeMessageChannelId = memberWelcomeChannel.Id,
            ApplicationChannelNameFormat = applicationChannelFormat,
            NewbieWelcomeTemplate = newbieWelcomeTemplate,
            MemberWelcomeTemplate = memberWelcomeTemplate,
            AutoAssignStrangerRoleOnJoin = autoAssignStrangerRole,
            EnableOnboardingWorkflow = enableOnboardingWorkflow,
            ApplicationModeratorRoleIds = []
        };

        if (idleKickMinutes > 0)
        {
            config.IdleKickTimeSpan = TimeSpan.FromMinutes(idleKickMinutes);
        }

        if (honeypotChannel != null)
        {
            config.HoneypotChannelId = honeypotChannel.Id;
        }

        if (moderatorRole != null)
        {
            config.ApplicationModeratorRoleIds.Add(moderatorRole.Id);
        }

        config.Questionnaires["Member"] = new Questionnaire
        {
            Id = "Member",
            Name = "On-boarding",
            Description = "New members on-boarding process",
            SubmissionChannelId = strangerStatusChannel.Id,
            Questions = [new Question { Content = "Have you read and understood the server rules?" }]
        };

        await guildConfigService.SaveAsync(config);

        DiscordEmbedBuilder embed = new()
        {
            Title = "Configuration saved",
            Description = "This server is now configured. The bot will process new members and applications.",
            Color = new DiscordColor(0x00FF00)
        };
        embed.AddField("Stranger role", strangerRole.Mention, true);
        embed.AddField("Member role", memberRole.Mention, true);
        embed.AddField("Application category", applicationCategory.Mention, true);

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    [SlashCommand("setup-honeypot", "Honeypot-only setup for servers that do not need onboarding.")]
    public async Task SetupHoneypot(
        InteractionContext ctx,
        [Option("honeypot_channel", "Channel that bans users who post in it")]
        DiscordChannel honeypotChannel,
        [Option("honeypot_exclusion_role", "Role exempt from honeypot ban (optional)")]
        DiscordRole honeypotExclusionRole = null
    )
    {
        await ctx.DeferAsync();

        GuildConfig? existing = await guildConfigService.GetAsync(ctx.Guild.Id);
        GuildConfig config;

        if (existing is not null)
        {
            config = existing;
            config.HoneypotChannelId = honeypotChannel.Id;
            config.EnableOnboardingWorkflow = false;
            if (honeypotExclusionRole is not null && !config.HoneypotExclusionRoleIds.Contains(honeypotExclusionRole.Id))
            {
                config.HoneypotExclusionRoleIds.Add(honeypotExclusionRole.Id);
            }
        }
        else
        {
            config = new GuildConfig
            {
                GuildId = ctx.Guild.Id,
                HoneypotChannelId = honeypotChannel.Id,
                EnableOnboardingWorkflow = false
            };
            if (honeypotExclusionRole is not null)
            {
                config.HoneypotExclusionRoleIds.Add(honeypotExclusionRole.Id);
            }
        }

        await guildConfigService.SaveAsync(config);

        DiscordEmbedBuilder embed = new()
        {
            Title = "Honeypot configured",
            Description = "Users who post in the honeypot channel will be banned. Configured exclusion roles are exempt.",
            Color = new DiscordColor(0x00FF00)
        };
        embed.AddField("Honeypot channel", honeypotChannel.Mention, true);
        if (honeypotExclusionRole is not null)
        {
            embed.AddField("Exclusion role", honeypotExclusionRole.Mention, true);
        }

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    [SlashCommand("view", "View current configuration for this server.")]
    public async Task View(InteractionContext ctx)
    {
        await ctx.DeferAsync(true);

        GuildConfig? config = await guildConfigService.GetAsync(ctx.Guild.Id);

        if (config is null)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Not configured",
                Description = "This server has no configuration yet. Run `/config setup` to get started.",
                Color = new DiscordColor(0xFFAA00)
            }));
            return;
        }

        DiscordEmbedBuilder embed = new() { Title = "Server configuration", Color = new DiscordColor(0x3498DB) };
        embed.AddField("Stranger role", config.StrangerRoleId != 0 ? $"<@&{config.StrangerRoleId}>" : "Not set", true);
        embed.AddField("Member role", config.MemberRoleId != 0 ? $"<@&{config.MemberRoleId}>" : "Not set", true);
        embed.AddField("Application category", config.ApplicationCategoryId != 0 ? $"<#{config.ApplicationCategoryId}>" : "Not set", true);
        embed.AddField("Stranger status channel", config.StrangerStatusChannelId != 0 ? $"<#{config.StrangerStatusChannelId}>" : "Not set", true);
        embed.AddField("Member welcome channel", config.MemberWelcomeMessageChannelId != 0 ? $"<#{config.MemberWelcomeMessageChannelId}>" : "Not set", true);
        embed.AddField("Channel format", config.ApplicationChannelNameFormat ?? "newbie-{0:D4}", true);
        embed.AddField("Auto-assign stranger role", config.AutoAssignStrangerRoleOnJoin ? "Yes" : "No", true);
        embed.AddField("Idle kick", config.IdleKickTimeSpan?.ToString() ?? "Disabled", true);
        embed.AddField("Honeypot", config.HoneypotChannelId.HasValue ? $"<#{config.HoneypotChannelId}>" : "Not set",
            true);
        embed.AddField("Onboarding workflow", config.EnableOnboardingWorkflow ? "Enabled" : "Disabled", true);

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    [SlashCommand("set", "Update a single configuration option.")]
    public async Task Set(
        InteractionContext ctx,
        [Option("option", "Which option to update")]
        ConfigOption option,
        [Option("value", "New value (role/channel mention or text)")]
        string value
    )
    {
        await ctx.DeferAsync(true);

        GuildConfig? config = await guildConfigService.GetAsync(ctx.Guild.Id);

        if (config is null)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Not configured",
                Description = "Run `/config setup` or `/config setup-honeypot` first to create initial configuration.",
                Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        ulong? parsedId = ParseSnowflake(value);
        string validationError = null;

        switch (option)
        {
            case ConfigOption.StrangerRole:
                validationError = ValidateRole(ctx.Guild, parsedId, out ulong strangerRoleId);
                if (validationError is null)
                {
                    config.StrangerRoleId = strangerRoleId;
                }

                break;
            case ConfigOption.MemberRole:
                validationError = ValidateRole(ctx.Guild, parsedId, out ulong memberRoleId);
                if (validationError is null)
                {
                    config.MemberRoleId = memberRoleId;
                }

                break;
            case ConfigOption.ApplicationCategory:
                validationError = ValidateCategoryChannel(ctx.Guild, parsedId, out ulong categoryId);
                if (validationError is null)
                {
                    config.ApplicationCategoryId = categoryId;
                }

                break;
            case ConfigOption.StrangerStatusChannel:
            case ConfigOption.MemberWelcomeChannel:
                validationError = ValidateTextChannel(ctx.Guild, parsedId, out ulong channelId);
                if (validationError is null)
                {
                    if (option == ConfigOption.StrangerStatusChannel)
                    {
                        config.StrangerStatusChannelId = channelId;
                    }
                    else
                    {
                        config.MemberWelcomeMessageChannelId = channelId;
                    }
                }

                break;
            case ConfigOption.ApplicationChannelFormat:
                validationError = ValidateApplicationChannelFormat(value);
                if (validationError is null)
                {
                    config.ApplicationChannelNameFormat = value;
                }

                break;
            case ConfigOption.NewbieWelcomeTemplate:
                validationError = ValidateMentionTemplate(value);
                if (validationError is null)
                {
                    config.NewbieWelcomeTemplate = value;
                }

                break;
            case ConfigOption.MemberWelcomeTemplate:
                validationError = ValidateMentionTemplate(value);
                if (validationError is null)
                {
                    config.MemberWelcomeTemplate = value;
                }

                break;
            case ConfigOption.AutoAssignStrangerRole:
                if (!bool.TryParse(value, out bool b))
                {
                    validationError = "Value must be 'true' or 'false'.";
                }
                else
                {
                    config.AutoAssignStrangerRoleOnJoin = b;
                }

                break;
            case ConfigOption.IdleKickMinutes:
                if (!long.TryParse(value, out long m) || m <= 0)
                {
                    validationError = "Value must be a positive number (minutes). Use 0 or omit to disable idle kick.";
                }
                else
                {
                    config.IdleKickTimeSpan = TimeSpan.FromMinutes(m);
                }

                break;
            case ConfigOption.HoneypotChannel:
                if (!parsedId.HasValue)
                {
                    config.HoneypotChannelId = null;
                }
                else
                {
                    validationError = ValidateTextChannel(ctx.Guild, parsedId, out ulong honeypotId);
                    if (validationError is null)
                    {
                        config.HoneypotChannelId = honeypotId;
                    }
                }

                break;
            case ConfigOption.ModeratorRole:
                validationError = ValidateRole(ctx.Guild, parsedId, out ulong modRoleId);
                if (validationError is null && !config.ApplicationModeratorRoleIds.Contains(modRoleId))
                {
                    config.ApplicationModeratorRoleIds.Add(modRoleId);
                }

                break;
            case ConfigOption.EnableOnboardingWorkflow:
                if (!bool.TryParse(value, out bool enableWorkflow))
                {
                    validationError = "Value must be 'true' or 'false'.";
                }
                else
                {
                    config.EnableOnboardingWorkflow = enableWorkflow;
                }

                break;
            case ConfigOption.HoneypotExclusionRole:
                validationError = ValidateRole(ctx.Guild, parsedId, out ulong exclusionRoleId);
                if (validationError is null && !config.HoneypotExclusionRoleIds.Contains(exclusionRoleId))
                {
                    config.HoneypotExclusionRoleIds.Add(exclusionRoleId);
                }

                break;
            case ConfigOption.HoneypotExclusionRoleRemove:
                validationError = ValidateRole(ctx.Guild, parsedId, out ulong removeExclusionRoleId);
                if (validationError is null)
                {
                    config.HoneypotExclusionRoleIds.Remove(removeExclusionRoleId);
                }

                break;
            default:
                validationError =
                    $"Could not parse value for {option}. Use a role or channel mention, or appropriate text.";
                break;
        }

        if (validationError is not null)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Invalid value", Description = validationError, Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        await guildConfigService.SaveAsync(config);

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
        {
            Title = "Updated", Description = $"Set {option} successfully.", Color = new DiscordColor(0x00FF00)
        }));
    }

    private static string ValidateRole(DiscordGuild guild, ulong? parsedId, out ulong roleId)
    {
        roleId = 0;
        if (!parsedId.HasValue)
        {
            return "Provide a role mention (e.g. <@&role_id>).";
        }

        try
        {
            DiscordRole role = guild.GetRole(parsedId.Value);
            if (role is null)
            {
                return "Role not found in this server.";
            }

            roleId = role.Id;
            return null;
        }
        catch
        {
            return "Role not found in this server.";
        }
    }

    private static string ValidateCategoryChannel(DiscordGuild guild, ulong? parsedId, out ulong channelId)
    {
        channelId = 0;
        if (!parsedId.HasValue)
        {
            return "Provide a category channel mention (e.g. <#channel_id>).";
        }

        try
        {
            DiscordChannel channel = guild.GetChannel(parsedId.Value);
            if (channel is null || channel.Type != ChannelType.Category)
            {
                return "Category channel not found or not a category.";
            }

            channelId = channel.Id;
            return null;
        }
        catch
        {
            return "Category channel not found in this server.";
        }
    }

    private static string ValidateTextChannel(DiscordGuild guild, ulong? parsedId, out ulong channelId)
    {
        channelId = 0;
        if (!parsedId.HasValue)
        {
            return "Provide a channel mention (e.g. <#channel_id>).";
        }

        try
        {
            DiscordChannel channel = guild.GetChannel(parsedId.Value);
            if (channel is null)
            {
                return "Channel not found in this server.";
            }

            channelId = channel.Id;
            return null;
        }
        catch
        {
            return "Channel not found in this server.";
        }
    }

    private static string ValidateApplicationChannelFormat(string value)
    {
        try
        {
            _ = string.Format(value, 1);
            return null;
        }
        catch (FormatException ex)
        {
            return $"Invalid format string: {ex.Message}. Use {{0}} for the channel number (e.g. newbie-{{0:D4}}).";
        }
    }

    private static string ValidateMentionTemplate(string value)
    {
        try
        {
            _ = string.Format(value, "@user");
            return null;
        }
        catch (FormatException ex)
        {
            return $"Invalid template: {ex.Message}. Use {{0}} for the member mention.";
        }
    }

    private static ulong? ParseSnowflake(string value)
    {
        value = value.Trim();
        if (value.StartsWith("<@&") && value.EndsWith(">"))
        {
            value = value[3..^1];
        }
        else if (value.StartsWith("<#") && value.EndsWith(">"))
        {
            value = value[2..^1];
        }
        else if (value.StartsWith("<@") && value.EndsWith(">"))
        {
            value = value[2..^1];
            if (value.Contains('!'))
            {
                value = value[(value.IndexOf('!') + 1)..];
            }
        }

        return ulong.TryParse(value, out ulong id) ? id : null;
    }
}

/// <summary>
///     Configuration options that can be updated via /config set.
/// </summary>
public enum ConfigOption
{
    [ChoiceName("Stranger role")] StrangerRole,

    [ChoiceName("Member role")] MemberRole,

    [ChoiceName("Application category")] ApplicationCategory,

    [ChoiceName("Stranger status channel")]
    StrangerStatusChannel,

    [ChoiceName("Member welcome channel")] MemberWelcomeChannel,

    [ChoiceName("Application channel format")]
    ApplicationChannelFormat,

    [ChoiceName("Newbie welcome template")]
    NewbieWelcomeTemplate,

    [ChoiceName("Member welcome template")]
    MemberWelcomeTemplate,

    [ChoiceName("Auto-assign stranger role")]
    AutoAssignStrangerRole,

    [ChoiceName("Idle kick minutes")] IdleKickMinutes,

    [ChoiceName("Honeypot channel")] HoneypotChannel,

    [ChoiceName("Moderator role (add)")] ModeratorRole,

    [ChoiceName("Enable onboarding workflow")]
    EnableOnboardingWorkflow,

    [ChoiceName("Honeypot exclusion role (add)")]
    HoneypotExclusionRole,

    [ChoiceName("Honeypot exclusion role (remove)")]
    HoneypotExclusionRoleRemove
}