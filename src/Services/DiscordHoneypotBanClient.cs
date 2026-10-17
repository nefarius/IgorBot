using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

namespace IgorBot.Services;

/// <summary>
///     Live Discord implementation of <see cref="IHoneypotBanClient" />.
/// </summary>
internal sealed class DiscordHoneypotBanClient(DiscordClient discord) : IHoneypotBanClient
{
    internal const string BanReason = "User fell into honeypot trap";

    public async Task BanAsync(ulong guildId, ulong userId)
    {
        if (!discord.Guilds.TryGetValue(guildId, out DiscordGuild? guild))
        {
            throw new InvalidOperationException($"Guild {guildId} is not present in the Discord client cache.");
        }

        // A zero purge window leaves history intact. HoneypotMessageCleanup deletes the
        // triggering message, including posts older than Discord's purge maximum.
        await guild.BanMemberAsync(userId, TimeSpan.Zero, BanReason);
    }

    public async Task<bool?> IsMemberPresentAsync(ulong guildId, ulong userId)
    {
        if (!discord.Guilds.TryGetValue(guildId, out DiscordGuild? guild))
        {
            return null;
        }

        try
        {
            await guild.GetMemberAsync(userId);
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
        catch
        {
            return null;
        }
    }
}
