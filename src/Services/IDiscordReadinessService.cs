namespace IgorBot.Services;

/// <summary>
///     Tracks which guilds have finished downloading into the Discord client cache.
///     Used to distinguish the startup-race window from a guild the bot has left.
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
    ///     via <see cref="MarkGuildAvailable" /> since the gateway session became ready.
    /// </summary>
    bool IsGuildReady(ulong guildId);

    /// <summary>
    ///     Clears readiness. Called when the gateway connects or disconnects so a previous
    ///     session cannot mark a guild ready before the new cache download completes.
    /// </summary>
    void Reset();
}
