using MongoDB.Bson.Serialization.Attributes;
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

    public string GenerateNewID()
    {
        return $"{GuildId}";
    }

    [BsonId]
    public string ID { get; set; }
}