using System.Collections.Concurrent;

using IgorBot.Core;
using IgorBot.Schema;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace IgorBot.Services;

/// <summary>
///     Caches <see cref="GuildConfig" /> lookups with a short TTL to reduce load on the hot path (e.g. HoneypotModule).
///     Invalidates cache when config is saved.
/// </summary>
internal sealed class CachedGuildConfigService(IGuildConfigService inner, ILogger<CachedGuildConfigService> logger)
    : IGuildConfigService
{
    private const int CacheTtlSeconds = 60;

    private readonly ConcurrentDictionary<ulong, CachedEntry> _cache = new();

    public async Task<GuildConfig?> GetAsync(ulong guildId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(guildId, out CachedEntry? entry) && !entry.IsExpired())
        {
            return Clone(entry.Config);
        }

        GuildConfig? config = await inner.GetAsync(guildId, ct);
        if (config is not null)
        {
            _cache[guildId] = new CachedEntry(Clone(config), CacheTtlSeconds);
        }
        else
        {
            _cache.TryRemove(guildId, out _);
        }

        return config;
    }

    public Task<IReadOnlyList<GuildConfig>> GetAllAsync(CancellationToken ct = default)
    {
        return inner.GetAllAsync(ct);
    }

    public async Task SaveAsync(GuildConfig config, CancellationToken ct = default)
    {
        try
        {
            await inner.SaveAsync(config, ct);
        }
        finally
        {
            // Also on failure (e.g. a version conflict): the cached copy may be the stale one.
            if (_cache.TryRemove(config.GuildId, out _))
            {
                logger.LogDebug("Invalidated guild config cache for {GuildId}", config.GuildId);
            }
        }
    }

    /// <summary>
    ///     Callers mutate the config they get (commands) while others read it concurrently (honeypot, jobs), so every
    ///     caller receives its own deep copy.
    /// </summary>
    internal static GuildConfig Clone(GuildConfig config)
    {
        BsonDocument document = GuildConfigEntity.FromGuildConfig(config).ToBsonDocument();
        return BsonSerializer.Deserialize<GuildConfigEntity>(document).ToGuildConfig();
    }

    private sealed class CachedEntry
    {
        private readonly DateTime _expiresAt;

        internal CachedEntry(GuildConfig config, int ttlSeconds)
        {
            Config = config;
            _expiresAt = DateTime.UtcNow.AddSeconds(ttlSeconds);
        }

        internal GuildConfig Config { get; }

        internal bool IsExpired()
        {
            return DateTime.UtcNow >= _expiresAt;
        }
    }
}