using DSharpPlus.Commands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace IgorBot.Util;

internal static class DiscordExtensions
{
    internal static string ToEntityId(this GuildMemberRemovedEventArgs e) =>
        DiscordEntityIds.ForMember(e.Guild.Id, e.Member.Id);

    internal static string ToEntityId(this GuildMemberAddedEventArgs e) =>
        DiscordEntityIds.ForMember(e.Guild.Id, e.Member.Id);

    internal static string ToEntityId(this GuildMemberUpdatedEventArgs e) =>
        DiscordEntityIds.ForMember(e.Guild.Id, e.Member.Id);

    internal static string ToEntityId(this CommandContext e)
    {
        DiscordGuild guild = e.RequireGuild();
        return DiscordEntityIds.ForMember(guild.Id, e.RequireMember().Id);
    }

    internal static string ToEntityId(this DiscordMember e) =>
        DiscordEntityIds.ForMember(e.Guild?.Id ?? throw new InvalidOperationException("Member has no guild."), e.Id);

    internal static DiscordGuild RequireGuild(this CommandContext context) =>
        context.Guild ?? throw new InvalidOperationException("This command must be used in a server.");

    internal static DiscordMember RequireMember(this CommandContext context) =>
        context.Member ?? throw new InvalidOperationException("This command requires a server member.");

    internal static DiscordChannel? FindChannel(this DiscordGuild guild, ulong channelId) =>
        guild.Channels.TryGetValue(channelId, out DiscordChannel? channel) ? channel : null;

    internal static async Task<List<T>> MaterializeAsync<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
    {
        List<T> items = [];
        await foreach (T item in source.WithCancellation(cancellationToken))
        {
            items.Add(item);
        }

        return items;
    }
}
