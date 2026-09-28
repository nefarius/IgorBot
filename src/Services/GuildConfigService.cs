using IgorBot.Core;
using IgorBot.Schema;

using MongoDB.Driver;
using MongoDB.Entities;

namespace IgorBot.Services;

/// <summary>
///     MongoDB-backed implementation of <see cref="IGuildConfigService" />.
/// </summary>
internal sealed class GuildConfigService(DB db) : IGuildConfigService
{
    private const int MaxSaveAttempts = 3;

    public async Task<GuildConfig?> GetAsync(ulong guildId, CancellationToken ct = default)
    {
        GuildConfigEntity? entity = await db.Find<GuildConfigEntity>()
            .OneAsync(guildId.ToString(), ct);

        return entity?.ToGuildConfig();
    }

    public async Task<IReadOnlyList<GuildConfig>> GetAllAsync(CancellationToken ct = default)
    {
        List<GuildConfigEntity> entities = await db.Find<GuildConfigEntity>()
            .ExecuteAsync(ct);

        return entities.Select(e => e.ToGuildConfig()).ToList();
    }

    public async Task SaveAsync(GuildConfig config, CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < MaxSaveAttempts; attempt++)
        {
            GuildConfigEntity? previous = await db.Find<GuildConfigEntity>()
                .OneAsync(config.GuildId.ToString(), ct);

            if (IsStaleHoneypotOverwrite(previous, config))
            {
                config.HoneypotChannelId = previous!.HoneypotChannelId;
                config.HoneypotChannelActivatedAt = previous.HoneypotChannelActivatedAt;
            }

            DateTime? activation = previous?.HoneypotChannelActivatedAt;
            if (previous?.HoneypotChannelId != config.HoneypotChannelId)
            {
                activation = config.HoneypotChannelId.HasValue ? DateTime.UtcNow : null;
            }

            config.HoneypotChannelActivatedAt = activation;

            GuildConfigEntity entity = GuildConfigEntity.FromGuildConfig(config);
            entity.ID = config.GuildId.ToString();
            entity.ConfigVersion = (previous?.ConfigVersion ?? 0) + 1;

            if (previous is null)
            {
                await db.SaveAsync(entity, ct);
                return;
            }

            ReplaceOneResult result = await db.Replace<GuildConfigEntity>()
                .Match(e => e.ID == entity.ID && e.ConfigVersion == previous.ConfigVersion)
                .WithEntity(entity)
                .ExecuteAsync(ct);

            if (result.ModifiedCount == 1)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Could not save guild config {config.GuildId} due to concurrent updates.");
    }

    /// <summary>
    ///     A caller loaded an older snapshot whose honeypot channel no longer matches the
    ///     stored document, and that snapshot's activation is older than the stored one.
    /// </summary>
    private static bool IsStaleHoneypotOverwrite(GuildConfigEntity? previous, GuildConfig incoming)
    {
        if (previous is null || previous.HoneypotChannelId == incoming.HoneypotChannelId)
        {
            return false;
        }

        return previous.HoneypotChannelActivatedAt is DateTime stored
               && incoming.HoneypotChannelActivatedAt is DateTime incomingAt
               && incomingAt < stored;
    }
}
