using IgorBot.Core;
using IgorBot.Schema;

using MongoDB.Entities;

namespace IgorBot.Services;

/// <summary>
///     MongoDB-backed implementation of <see cref="IGuildConfigService" />.
/// </summary>
internal sealed class GuildConfigService(DB db) : IGuildConfigService
{
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
        GuildConfigEntity entity = GuildConfigEntity.FromGuildConfig(config);
        entity.ID = config.GuildId.ToString();
        await db.SaveAsync(entity, ct);
    }
}