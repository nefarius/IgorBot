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
        [Option("stranger_role", "Role assigned to new members before they complete onboarding")] DiscordRole strangerRole,
        [Option("member_role", "Role assigned when a member is promoted")] DiscordRole memberRole,
        [Option("application_category", "Category where newbie channels are created")] DiscordChannel applicationCategory,
        [Option("stranger_status_channel", "Channel where application status embeds appear")] DiscordChannel strangerStatusChannel,
        [Option("member_welcome_channel", "Channel where welcome messages for promoted members appear")] DiscordChannel memberWelcomeChannel,
        [Option("application_channel_format", "Format for newbie channel names, use {0} for number")] string applicationChannelFormat = "newbie-{0:D4}",
        [Option("newbie_welcome_template", "Welcome message template, use {0} for member mention")] string newbieWelcomeTemplate = "Welcome, {0}! Before you can become a full member, we wanna know a bit about you. Please enter **/apply member** to start!",
        [Option("member_welcome_template", "Welcome message for promoted members, use {0} for member mention")] string memberWelcomeTemplate = "Welcome {0}, enjoy your stay!",
        [Option("auto_assign_stranger_role", "Automatically assign stranger role when member joins")] bool autoAssignStrangerRole = false,
        [Option("idle_kick_minutes", "Minutes before kicking inactive strangers (0 or omit to disable)")] long idleKickMinutes = 0,
        [Option("honeypot_channel", "Channel that bans users who post in it (optional)")] DiscordChannel honeypotChannel = null,
        [Option("moderator_role", "Role that can see and interact with newbie channels (optional)")] DiscordRole moderatorRole = null
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

        DiscordEmbedBuilder embed = new()
        {
            Title = "Server configuration",
            Color = new DiscordColor(0x3498DB)
        };
        embed.AddField("Stranger role", $"<@&{config.StrangerRoleId}>", true);
        embed.AddField("Member role", $"<@&{config.MemberRoleId}>", true);
        embed.AddField("Application category", $"<#{config.ApplicationCategoryId}>", true);
        embed.AddField("Stranger status channel", $"<#{config.StrangerStatusChannelId}>", true);
        embed.AddField("Member welcome channel", $"<#{config.MemberWelcomeMessageChannelId}>", true);
        embed.AddField("Channel format", config.ApplicationChannelNameFormat ?? "newbie-{0:D4}", true);
        embed.AddField("Auto-assign stranger role", config.AutoAssignStrangerRoleOnJoin ? "Yes" : "No", true);
        embed.AddField("Idle kick", config.IdleKickTimeSpan?.ToString() ?? "Disabled", true);
        embed.AddField("Honeypot", config.HoneypotChannelId.HasValue ? $"<#{config.HoneypotChannelId}>" : "Not set", true);

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    [SlashCommand("set", "Update a single configuration option.")]
    public async Task Set(
        InteractionContext ctx,
        [Option("option", "Which option to update")] ConfigOption option,
        [Option("value", "New value (role/channel mention or text)")] string value
    )
    {
        await ctx.DeferAsync(true);

        GuildConfig? config = await guildConfigService.GetAsync(ctx.Guild.Id);

        if (config is null)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Not configured",
                Description = "Run `/config setup` first to create initial configuration.",
                Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        ulong? parsedId = ParseSnowflake(value);

        switch (option)
        {
            case ConfigOption.StrangerRole when parsedId.HasValue:
                config.StrangerRoleId = parsedId.Value;
                break;
            case ConfigOption.MemberRole when parsedId.HasValue:
                config.MemberRoleId = parsedId.Value;
                break;
            case ConfigOption.ApplicationCategory when parsedId.HasValue:
                config.ApplicationCategoryId = parsedId.Value;
                break;
            case ConfigOption.StrangerStatusChannel when parsedId.HasValue:
                config.StrangerStatusChannelId = parsedId.Value;
                break;
            case ConfigOption.MemberWelcomeChannel when parsedId.HasValue:
                config.MemberWelcomeMessageChannelId = parsedId.Value;
                break;
            case ConfigOption.ApplicationChannelFormat:
                config.ApplicationChannelNameFormat = value;
                break;
            case ConfigOption.NewbieWelcomeTemplate:
                config.NewbieWelcomeTemplate = value;
                break;
            case ConfigOption.MemberWelcomeTemplate:
                config.MemberWelcomeTemplate = value;
                break;
            case ConfigOption.AutoAssignStrangerRole:
                config.AutoAssignStrangerRoleOnJoin = bool.TryParse(value, out bool b) && b;
                break;
            case ConfigOption.IdleKickMinutes:
                config.IdleKickTimeSpan = long.TryParse(value, out long m) && m > 0
                    ? TimeSpan.FromMinutes(m)
                    : null;
                break;
            case ConfigOption.HoneypotChannel:
                config.HoneypotChannelId = parsedId;
                break;
            case ConfigOption.ModeratorRole when parsedId.HasValue:
                if (!config.ApplicationModeratorRoleIds.Contains(parsedId.Value))
                {
                    config.ApplicationModeratorRoleIds.Add(parsedId.Value);
                }
                break;
            default:
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
                {
                    Title = "Invalid value",
                    Description = $"Could not parse value for {option}. Use a role or channel mention, or appropriate text.",
                    Color = new DiscordColor(0xFF0000)
                }));
                return;
        }

        await guildConfigService.SaveAsync(config);

        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
        {
            Title = "Updated",
            Description = $"Set {option} successfully.",
            Color = new DiscordColor(0x00FF00)
        }));
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
    [ChoiceName("Stranger role")]
    StrangerRole,

    [ChoiceName("Member role")]
    MemberRole,

    [ChoiceName("Application category")]
    ApplicationCategory,

    [ChoiceName("Stranger status channel")]
    StrangerStatusChannel,

    [ChoiceName("Member welcome channel")]
    MemberWelcomeChannel,

    [ChoiceName("Application channel format")]
    ApplicationChannelFormat,

    [ChoiceName("Newbie welcome template")]
    NewbieWelcomeTemplate,

    [ChoiceName("Member welcome template")]
    MemberWelcomeTemplate,

    [ChoiceName("Auto-assign stranger role")]
    AutoAssignStrangerRole,

    [ChoiceName("Idle kick minutes")]
    IdleKickMinutes,

    [ChoiceName("Honeypot channel")]
    HoneypotChannel,

    [ChoiceName("Moderator role (add)")]
    ModeratorRole
}
