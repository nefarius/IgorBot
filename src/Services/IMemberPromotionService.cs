using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Schema;

namespace IgorBot.Services;

internal interface IMemberPromotionService
{
    Task PromoteAsync(
        DiscordGuild guild,
        DiscordMember member,
        GuildMember entry,
        GuildConfig guildConfig,
        ulong? actorId,
        string? reason,
        CancellationToken cancellationToken = default);
}
