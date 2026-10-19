using System.Diagnostics.CodeAnalysis;

using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Entities;

namespace IgorBot.Schema;

/// <summary>
///     Stores runtime data about the guild.
/// </summary>
public class GuildProperties : IEntity
{
    /// <summary>
    ///     The unique ID of the guild.
    /// </summary>
    public ulong GuildId { get; init; }

    /// <summary>
    ///     Incrementing counter to name the application channels.
    /// </summary>
    public ulong ApplicationChannels { get; set; } = 1;

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [BsonId]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public string ID { get; set; } = null!;

    object IEntity.GenerateNewID()
    {
        return GenerateNewID();
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public string GenerateNewID()
    {
        return $"{GuildId}";
    }

    public bool HasDefaultID()
    {
        return string.IsNullOrEmpty(ID);
    }

    /// <summary>
    ///     Atomically creates the per-guild document when missing (<c>$setOnInsert</c> upsert) and returns the
    ///     persisted one. Concurrent first users can neither collide nor reset an existing counter.
    /// </summary>
    public static async Task<GuildProperties> EnsureExistsAsync(DB db, ulong guildId)
    {
        string id = guildId.ToString();
        FilterDefinition<GuildProperties> filter = Builders<GuildProperties>.Filter.Eq(g => g.ID, id);
        UpdateDefinition<GuildProperties> update = Builders<GuildProperties>.Update
            .SetOnInsert(g => g.GuildId, guildId)
            .SetOnInsert(g => g.ApplicationChannels, 1UL);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await db.Collection<GuildProperties>().UpdateOneAsync(
                    filter, update, new UpdateOptions { IsUpsert = true });
                break;
            }
            catch (MongoCommandException ex) when (attempt == 0 && ex.Code == 11000)
            {
            }
            catch (MongoWriteException ex) when (attempt == 0 &&
                                                 ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
            }
        }

        return await db.Find<GuildProperties>().OneAsync(id)
               ?? throw new InvalidOperationException($"Guild properties {id} vanished after upsert.");
    }
}
