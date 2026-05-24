namespace IgorBot.Services;

/// <summary>
///     Tracks which guilds have fired their <c>GuildAvailable</c> event after the Discord
///     client connected. Used to distinguish the startup-race window (where
///     <see cref="Nefarius.DSharpPlus.Extensions.Hosting.IDiscordClientService" />'s
///     <c>Guilds</c> dictionary is not yet populated) from a genuine runtime condition
///     where the bot is no longer in a guild.
/// </summary>
internal interface IDiscordReadinessService
{
    /// <summary>
    ///     Records that <paramref name="guildId" /> has become available and its data is
    ///     present in the Discord client cache.
    /// </summary>
    void MarkGuildAvailable(ulong guildId);

    /// <summary>
    ///     Returns <c>true</c> when <paramref name="guildId" /> has been seen at least once
    ///     via <see cref="MarkGuildAvailable" /> since the process started.
    /// </summary>
    bool IsGuildReady(ulong guildId);
}
