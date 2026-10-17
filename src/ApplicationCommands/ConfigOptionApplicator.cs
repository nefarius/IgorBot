using IgorBot.Core;

namespace IgorBot.ApplicationCommands;

internal enum GuildEntityKind
{
    Missing,
    Role,
    Category,
    Channel
}

internal readonly record struct GuildEntityFacts(GuildEntityKind Kind);

/// <summary>
///     Applies one <c>/config set</c> value and returns the same validation text the command shows.
/// </summary>
internal static class ConfigOptionApplicator
{
    public static ulong? ParseSnowflake(string value)
    {
        value = value.Trim();
        if (value.StartsWith("<@&", StringComparison.Ordinal) && value.EndsWith('>'))
        {
            value = value[3..^1];
        }
        else if (value.StartsWith("<#", StringComparison.Ordinal) && value.EndsWith('>'))
        {
            value = value[2..^1];
        }
        else if (value.StartsWith("<@", StringComparison.Ordinal) && value.EndsWith('>'))
        {
            value = value[2..^1];
            int bang = value.IndexOf('!');
            if (bang >= 0)
            {
                value = value[(bang + 1)..];
            }
        }

        return ulong.TryParse(value, out ulong id) ? id : null;
    }

    public static string? TryApply(
        GuildConfig config,
        ConfigOption option,
        string value,
        Func<ulong, GuildEntityFacts> resolve)
    {
        ulong? parsedId = ParseSnowflake(value);

        return option switch
        {
            ConfigOption.StrangerRole => AssignRole(parsedId, resolve, id => config.StrangerRoleId = id),
            ConfigOption.MemberRole => AssignRole(parsedId, resolve, id => config.MemberRoleId = id),
            ConfigOption.ApplicationCategory => AssignCategory(parsedId, resolve, id => config.ApplicationCategoryId = id),
            ConfigOption.StrangerStatusChannel => AssignChannel(
                parsedId, resolve, id => config.StrangerStatusChannelId = id),
            ConfigOption.MemberWelcomeChannel => AssignChannel(
                parsedId, resolve, id => config.MemberWelcomeMessageChannelId = id),
            ConfigOption.ApplicationChannelFormat => ValidateApplicationChannelFormat(value) is { } formatError
                ? formatError
                : Assign(() => config.ApplicationChannelNameFormat = value),
            ConfigOption.NewbieWelcomeTemplate => ValidateMentionTemplate(value) is { } newbieError
                ? newbieError
                : Assign(() => config.NewbieWelcomeTemplate = value),
            ConfigOption.MemberWelcomeTemplate => ValidateMentionTemplate(value) is { } memberError
                ? memberError
                : Assign(() => config.MemberWelcomeTemplate = value),
            ConfigOption.AutoAssignStrangerRole => AssignBool(
                value, "Value must be 'true' or 'false'.", b => config.AutoAssignStrangerRoleOnJoin = b),
            ConfigOption.IdleKickMinutes => AssignIdleKick(config, value),
            ConfigOption.HoneypotChannel => AssignOptionalChannel(config, parsedId, resolve),
            ConfigOption.ModeratorRole => AssignRole(parsedId, resolve, id =>
            {
                if (!config.ApplicationModeratorRoleIds.Contains(id))
                {
                    config.ApplicationModeratorRoleIds.Add(id);
                }
            }),
            ConfigOption.EnableOnboardingWorkflow => AssignBool(
                value, "Value must be 'true' or 'false'.", b => config.EnableOnboardingWorkflow = b),
            ConfigOption.HoneypotExclusionRole => AssignRole(parsedId, resolve, id =>
            {
                if (!config.HoneypotExclusionRoleIds.Contains(id))
                {
                    config.HoneypotExclusionRoleIds.Add(id);
                }
            }),
            ConfigOption.HoneypotExclusionRoleRemove => AssignRole(parsedId, resolve, id =>
                config.HoneypotExclusionRoleIds.Remove(id)),
            _ => $"Could not parse value for {option}. Use a role or channel mention, or appropriate text."
        };
    }

    private static string? Assign(Action apply)
    {
        apply();
        return null;
    }

    private static string? AssignRole(ulong? parsedId, Func<ulong, GuildEntityFacts> resolve, Action<ulong> apply)
    {
        if (!parsedId.HasValue)
        {
            return "Provide a role mention (e.g. <@&role_id>).";
        }

        if (resolve(parsedId.Value).Kind != GuildEntityKind.Role)
        {
            return "Role not found in this server.";
        }

        apply(parsedId.Value);
        return null;
    }

    private static string? AssignCategory(ulong? parsedId, Func<ulong, GuildEntityFacts> resolve, Action<ulong> apply)
    {
        if (!parsedId.HasValue)
        {
            return "Provide a category channel mention (e.g. <#channel_id>).";
        }

        if (resolve(parsedId.Value).Kind != GuildEntityKind.Category)
        {
            return "Category channel not found or not a category.";
        }

        apply(parsedId.Value);
        return null;
    }

    private static string? AssignChannel(ulong? parsedId, Func<ulong, GuildEntityFacts> resolve, Action<ulong> apply)
    {
        if (!parsedId.HasValue)
        {
            return "Provide a channel mention (e.g. <#channel_id>).";
        }

        GuildEntityKind kind = resolve(parsedId.Value).Kind;
        if (kind is not (GuildEntityKind.Channel or GuildEntityKind.Category))
        {
            return "Channel not found in this server.";
        }

        apply(parsedId.Value);
        return null;
    }

    private static string? AssignOptionalChannel(
        GuildConfig config,
        ulong? parsedId,
        Func<ulong, GuildEntityFacts> resolve)
    {
        if (!parsedId.HasValue)
        {
            config.HoneypotChannelId = null;
            return null;
        }

        return AssignChannel(parsedId, resolve, id => config.HoneypotChannelId = id);
    }

    private static string? AssignBool(string value, string error, Action<bool> apply)
    {
        if (!bool.TryParse(value, out bool parsed))
        {
            return error;
        }

        apply(parsed);
        return null;
    }

    private static string? AssignIdleKick(GuildConfig config, string value)
    {
        if (!long.TryParse(value, out long minutes) || minutes <= 0)
        {
            return "Value must be a positive number (minutes). Use 0 or omit to disable idle kick.";
        }

        config.IdleKickTimeSpan = TimeSpan.FromMinutes(minutes);
        return null;
    }

    public static string? ValidateApplicationChannelFormat(string value)
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

    public static string? ValidateMentionTemplate(string value)
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
}
