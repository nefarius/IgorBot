using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

using Nefarius.DSharpPlus.Extensions.Hosting;

namespace IgorBot.Services;

/// <summary>
///     Live Discord implementation of <see cref="IHoneypotBanClient" />.
/// </summary>
internal sealed class DiscordHoneypotBanClient(IDiscordClientService discord) : IHoneypotBanClient
{
    internal const string BanReason = "User fell into honeypot trap";

    public async Task BanAsync(ulong guildId, ulong userId)
    {
        if (!discord.Client.Guilds.TryGetValue(guildId, out DiscordGuild? guild))
        {
            throw new InvalidOperationException($"Guild {guildId} is not present in the Discord client cache.");
        }

        // DSharpPlus 4.5.1 places delete_message_days in the query string and sends no JSON body.
        // Discord ignores that deprecated field, so a non-zero value does not purge history.
        // HoneypotMessageCleanup deletes the triggering message instead.
        await guild.BanMemberAsync(userId, 0, BanReason);
    }

    public async Task<bool?> IsMemberPresentAsync(ulong guildId, ulong userId)
    {
        if (!discord.Client.Guilds.TryGetValue(guildId, out DiscordGuild? guild))
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
