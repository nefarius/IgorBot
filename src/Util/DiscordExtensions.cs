using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;

namespace IgorBot.Util;

internal static class DiscordExtensions
{
    internal static string ToEntityId(this GuildMemberRemoveEventArgs e)
    {
        return $"{e.Guild.Id}-{e.Member.Id}";
    }

    internal static string ToEntityId(this GuildMemberAddEventArgs e)
    {
        return $"{e.Guild.Id}-{e.Member.Id}";
    }

    internal static string ToEntityId(this GuildMemberUpdateEventArgs e)
    {
        return $"{e.Guild.Id}-{e.Member.Id}";
    }

    internal static string ToEntityId(this InteractionContext e)
    {
        return $"{e.Guild.Id}-{e.Member.Id}";
    }
    
    internal static string ToEntityId(this DiscordMember e)
    {
        return $"{e.Guild.Id}-{e.Id}";
    }
}
