using IgorBot.Core;

namespace IgorBot.Services;

/// <summary>
///     Service for reading and persisting guild configuration from MongoDB.
/// </summary>
public interface IGuildConfigService
{
    /// <summary>
    ///     Gets the configuration for a guild, or null if not configured.
    /// </summary>
    Task<GuildConfig?> GetAsync(ulong guildId, CancellationToken ct = default);

    /// <summary>
    ///     Gets all configured guilds.
    /// </summary>
    Task<IReadOnlyList<GuildConfig>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    ///     Saves or updates guild configuration.
    /// </summary>
    Task SaveAsync(GuildConfig config, CancellationToken ct = default);
}