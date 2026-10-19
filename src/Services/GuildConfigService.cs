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
        // The version the caller based its edits on. 0 = blind overwrite (fresh /config setup, migration).
        int expectedVersion = config.ConfigVersion;

        for (int attempt = 0; attempt < MaxSaveAttempts; attempt++)
        {
            GuildConfigEntity? previous = await db.Find<GuildConfigEntity>()
                .OneAsync(config.GuildId.ToString(), ct);

            if (expectedVersion != 0 && previous is not null && previous.ConfigVersion != expectedVersion)
            {
                throw new ConfigConflictException(config.GuildId);
            }

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

            GuildConfigEntity entity = GuildConfigEntity.FromGuildConfig(config);
            entity.ID = config.GuildId.ToString();
            entity.HoneypotChannelActivatedAt = activation;
            entity.ConfigVersion = (previous?.ConfigVersion ?? 0) + 1;

            if (previous is null)
            {
                try
                {
                    // Insert only: a concurrent first save surfaces as a duplicate key instead of a silent replace.
                    await db.Collection<GuildConfigEntity>().InsertOneAsync(entity, cancellationToken: ct);
                    Publish(config, entity);
                    return;
                }
                catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
                {
                    // Lost the creation race; re-evaluate against the winner's document.
                    continue;
                }
            }

            ReplaceOneResult result = await db.Replace<GuildConfigEntity>()
                .Match(e => e.ID == entity.ID && e.ConfigVersion == previous.ConfigVersion)
                .WithEntity(entity)
                .ExecuteAsync(ct);

            if (result.ModifiedCount == 1)
            {
                Publish(config, entity);
                return;
            }

            if (expectedVersion != 0)
            {
                throw new ConfigConflictException(config.GuildId);
            }
        }

        throw new InvalidOperationException(
            $"Could not save guild config {config.GuildId} due to concurrent updates.");
    }

    /// <summary>
    ///     Mirrors the stamped values back to the caller's snapshot only after the write succeeded, so a failed
    ///     save never leaves a half-updated object behind.
    /// </summary>
    private static void Publish(GuildConfig config, GuildConfigEntity saved)
    {
        config.HoneypotChannelActivatedAt = saved.HoneypotChannelActivatedAt;
        config.ConfigVersion = saved.ConfigVersion;
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
