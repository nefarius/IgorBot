using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Services;
using IgorBot.Util;

namespace IgorBot.ApplicationCommands;

[Command("config"), Description("Configure the bot for this server. Administrator only.")]
[RequirePermissions(DiscordPermission.Administrator)]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public sealed class ConfigCommands(IGuildConfigService guildConfigService)
{
    [Command("setup"), Description("Initial setup or overwrite configuration for this server.")]
    public async Task Setup(
        SlashCommandContext ctx,
        [Parameter("stranger_role"), Description("Role assigned to new members before they complete onboarding")]
        DiscordRole strangerRole,
        [Parameter("member_role"), Description("Role assigned when a member is promoted")]
        DiscordRole memberRole,
        [Parameter("application_category"), Description("Category where newbie channels are created")]
        DiscordChannel applicationCategory,
        [Parameter("stranger_status_channel"), Description("Channel where application status embeds appear")]
        DiscordChannel strangerStatusChannel,
        [Parameter("member_welcome_channel"), Description("Channel where welcome messages for promoted members appear")]
        DiscordChannel memberWelcomeChannel,
        [Parameter("application_channel_format"), Description("Format for newbie channel names, use {0} for number")]
        string applicationChannelFormat = "newbie-{0:D4}",
        [Parameter("newbie_welcome_template"), Description("Welcome message template, use {0} for member mention")]
        string newbieWelcomeTemplate =
            "Welcome, {0}! Before you can become a full member, we wanna know a bit about you. Please enter **/apply member** to start!",
        [Parameter("member_welcome_template"), Description("Welcome message for promoted members, use {0} for member mention")]
        string memberWelcomeTemplate = "Welcome {0}, enjoy your stay!",
        [Parameter("auto_assign_stranger_role"), Description("Automatically assign stranger role when member joins")]
        bool autoAssignStrangerRole = false,
        [Parameter("idle_kick_minutes"), Description("Minutes before kicking inactive strangers (0 or omit to disable)")]
        long idleKickMinutes = 0,
        [Parameter("honeypot_channel"), Description("Channel that bans users who post in it (optional)")]
        DiscordChannel? honeypotChannel = null,
        [Parameter("moderator_role"), Description("Role that can see and interact with newbie channels (optional)")]
        DiscordRole? moderatorRole = null,
        [Parameter("enable_onboarding_workflow"), Description("Run onboarding workflow when member gets stranger role")]
        bool enableOnboardingWorkflow = true
    )
    {
        await ctx.DeferResponseAsync();

        GuildConfig config = new()
        {
            GuildId = ctx.RequireGuild().Id,
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

        if (honeypotChannel is not null)
        {
            config.HoneypotChannelId = honeypotChannel.Id;
        }

        if (moderatorRole is not null)
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

        await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(embed));
    }

    [Command("setup-honeypot"), Description("Honeypot-only setup for servers that do not need onboarding.")]
    public async Task SetupHoneypot(
        SlashCommandContext ctx,
        [Parameter("honeypot_channel"), Description("Channel that bans users who post in it")]
        DiscordChannel honeypotChannel,
        [Parameter("honeypot_exclusion_role"), Description("Role exempt from honeypot ban (optional)")]
        DiscordRole? honeypotExclusionRole = null
    )
    {
        await ctx.DeferResponseAsync();

        GuildConfig? existing = await guildConfigService.GetAsync(ctx.RequireGuild().Id);
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
                GuildId = ctx.RequireGuild().Id,
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

        await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(embed));
    }

    [Command("view"), Description("View current configuration for this server.")]
    public async Task View(SlashCommandContext ctx)
    {
        await ctx.DeferResponseAsync(true);

        GuildConfig? config = await guildConfigService.GetAsync(ctx.RequireGuild().Id);

        if (config is null)
        {
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
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

        await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(embed));
    }

    [Command("set"), Description("Update a single configuration option.")]
    public async Task Set(
        SlashCommandContext ctx,
        [Parameter("option"), Description("Which option to update")]
        ConfigOption option,
        [Parameter("value"), Description("New value (role/channel mention or text)")]
        string value
    )
    {
        await ctx.DeferResponseAsync(true);

        GuildConfig? config = await guildConfigService.GetAsync(ctx.RequireGuild().Id);

        if (config is null)
        {
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Not configured",
                Description = "Run `/config setup` or `/config setup-honeypot` first to create initial configuration.",
                Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        string? validationError = ConfigOptionApplicator.TryApply(
            config, option, value, id => Describe(ctx.RequireGuild(), id));

        if (validationError is not null)
        {
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Invalid value", Description = validationError, Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        await guildConfigService.SaveAsync(config);

        await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
        {
            Title = "Updated", Description = $"Set {option} successfully.", Color = new DiscordColor(0x00FF00)
        }));
    }

    private static GuildEntityFacts Describe(DiscordGuild guild, ulong id)
    {
        if (guild.Roles.ContainsKey(id))
        {
            return new GuildEntityFacts(GuildEntityKind.Role);
        }

        DiscordChannel? channel = guild.FindChannel(id);
        if (channel is null)
        {
            return new GuildEntityFacts(GuildEntityKind.Missing);
        }

        GuildEntityKind kind = channel.Type switch
        {
            DiscordChannelType.Category => GuildEntityKind.Category,
            DiscordChannelType.Text => GuildEntityKind.TextChannel,
            _ => GuildEntityKind.OtherChannel
        };
        return new GuildEntityFacts(kind);
    }
}

/// <summary>
///     Configuration options that can be updated via /config set.
/// </summary>
public enum ConfigOption
{
    [ChoiceDisplayName("Stranger role")] StrangerRole,

    [ChoiceDisplayName("Member role")] MemberRole,

    [ChoiceDisplayName("Application category")] ApplicationCategory,

    [ChoiceDisplayName("Stranger status channel")]
    StrangerStatusChannel,

    [ChoiceDisplayName("Member welcome channel")] MemberWelcomeChannel,

    [ChoiceDisplayName("Application channel format")]
    ApplicationChannelFormat,

    [ChoiceDisplayName("Newbie welcome template")]
    NewbieWelcomeTemplate,

    [ChoiceDisplayName("Member welcome template")]
    MemberWelcomeTemplate,

    [ChoiceDisplayName("Auto-assign stranger role")]
    AutoAssignStrangerRole,

    [ChoiceDisplayName("Idle kick minutes")] IdleKickMinutes,

    [ChoiceDisplayName("Honeypot channel")] HoneypotChannel,

    [ChoiceDisplayName("Moderator role (add)")] ModeratorRole,

    [ChoiceDisplayName("Enable onboarding workflow")]
    EnableOnboardingWorkflow,

    [ChoiceDisplayName("Honeypot exclusion role (add)")]
    HoneypotExclusionRole,

    [ChoiceDisplayName("Honeypot exclusion role (remove)")]
    HoneypotExclusionRoleRemove
}