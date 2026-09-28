namespace IgorBot.Services;

/// <summary>
///     Discord side-effects required by honeypot enforcement. Isolated so tests can
///     exercise the DB-before-ban flow without a live Discord client.
/// </summary>
internal interface IHoneypotBanClient
{
    /// <summary>
    ///     Bans <paramref name="userId" /> from <paramref name="guildId" /> and purges one day of messages.
    /// </summary>
    Task BanAsync(ulong guildId, ulong userId);

    /// <summary>
    ///     Returns <see langword="true" /> if the member is still in the guild,
    ///     <see langword="false" /> if they are gone, or <see langword="null" /> if presence
    ///     cannot be confirmed (typically a transient Discord error).
    /// </summary>
    Task<bool?> IsMemberPresentAsync(ulong guildId, ulong userId);
}
