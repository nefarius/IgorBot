using System.Diagnostics.CodeAnalysis;

using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Entities;

namespace IgorBot.Schema;

/// <summary>
///     Holds details about a strangers newbie application channel.
/// </summary>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
internal sealed class NewbieChannel : IEntity
{
    /// <summary>
    ///      Snowflake ID of the Guild.
    /// </summary>
    public ulong GuildId { get; init; }

    /// <summary>
    ///     The ID identifying the channel.
    /// </summary>
    public ulong ChannelId { get; init; }

    /// <summary>
    ///     Display name of the channel.
    /// </summary>
    public string ChannelName { get; init; }

    /// <summary>
    ///     Channel mention string.
    /// </summary>
    public string Mention { get; init; }

    /// <summary>
    ///     Creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public override string ToString()
    {
        return $"Channel {ChannelName} ({ChannelId})";
    }

    public string GenerateNewID()
    {
        return $"{GuildId}-{ChannelId}";
    }

    [BsonId]
    public string ID { get; set; }
}