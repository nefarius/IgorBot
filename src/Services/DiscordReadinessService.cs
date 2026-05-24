using System.Collections.Concurrent;

namespace IgorBot.Services;

/// <inheritdoc />
internal sealed class DiscordReadinessService : IDiscordReadinessService
{
    private readonly ConcurrentDictionary<ulong, bool> _readyGuilds = new();

    /// <inheritdoc />
    public void MarkGuildAvailable(ulong guildId) => _readyGuilds[guildId] = true;

    /// <inheritdoc />
    public bool IsGuildReady(ulong guildId) => _readyGuilds.ContainsKey(guildId);
}
