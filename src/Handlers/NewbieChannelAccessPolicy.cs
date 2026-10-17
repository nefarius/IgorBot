using DSharpPlus.Entities;

namespace IgorBot.Handlers;

/// <summary>
///     Permission sets applied to a private newbie channel.
/// </summary>
internal static class NewbieChannelAccessPolicy
{
    public static IReadOnlyList<DiscordPermission> EveryoneDenied { get; } =
        [DiscordPermission.ViewChannel];

    public static IReadOnlyList<DiscordPermission> ModeratorAllowed { get; } =
    [
        DiscordPermission.ViewChannel,
        DiscordPermission.ReadMessageHistory,
        DiscordPermission.ManageMessages,
        DiscordPermission.SendMessages,
        DiscordPermission.EmbedLinks,
        DiscordPermission.AddReactions
    ];

    public static IReadOnlyList<DiscordPermission> MemberAllowed { get; } =
    [
        DiscordPermission.ViewChannel,
        DiscordPermission.ReadMessageHistory,
        DiscordPermission.SendMessages,
        DiscordPermission.AttachFiles,
        DiscordPermission.EmbedLinks,
        DiscordPermission.AddReactions
    ];

    public static DiscordPermissions ToPermissions(IReadOnlyList<DiscordPermission> permissions)
    {
        DiscordPermissions combined = DiscordPermissions.None;
        foreach (DiscordPermission permission in permissions)
        {
            combined |= permission;
        }

        return combined;
    }
}
