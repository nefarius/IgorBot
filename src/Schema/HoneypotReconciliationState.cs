using System.Diagnostics.CodeAnalysis;

using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Entities;

namespace IgorBot.Schema;

/// <summary>
///     Durable per-guild honeypot channel cursor used to resume history scans after outages.
/// </summary>
internal sealed class HoneypotReconciliationState : IEntity
{
    /// <summary>
    ///     Guild snowflake ID.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    ///     Honeypot channel snowflake ID this cursor belongs to.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    ///     Last Discord message ID that was fully evaluated. Subsequent scans fetch messages after this ID.
    /// </summary>
    public ulong LastProcessedMessageId { get; set; }

    /// <summary>
    ///     Oldest message ID of a newer page that is not yet contiguous with
    ///     <see cref="LastProcessedMessageId" />. The next tick continues
    ///     <c>GetMessagesBeforeAsync</c> from this ID.
    /// </summary>
    public ulong? ContinuationBeforeMessageId { get; set; }

    /// <summary>
    ///     UTC timestamp of the last successful checkpoint write.
    /// </summary>
    public DateTime LastReconciledAt { get; set; } = DateTime.UtcNow;

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [BsonId]
    public string ID { get; set; } = null!;

    object IEntity.GenerateNewID()
    {
        return GenerateNewID();
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public string GenerateNewID()
    {
        return $"{GuildId}-{ChannelId}";
    }

    public bool HasDefaultID()
    {
        return string.IsNullOrEmpty(ID);
    }
}
