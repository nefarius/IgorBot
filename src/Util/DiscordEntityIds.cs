namespace IgorBot.Util;

internal static class DiscordEntityIds
{
    public static string ForMember(ulong guildId, ulong memberId) => $"{guildId}-{memberId}";
}
