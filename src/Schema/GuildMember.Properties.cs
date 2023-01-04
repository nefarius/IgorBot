using System.Diagnostics.CodeAnalysis;

using MongoDB.Bson.Serialization.Attributes;

namespace IgorBot.Schema;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
internal sealed partial class GuildMember
{
    /// <summary>
    ///     Discord user/member ID.
    /// </summary>
    public ulong MemberId { get; init; }

    /// <summary>
    ///     Guild ID.
    /// </summary>
    public ulong GuildId { get; init; }

    /// <summary>
    ///     The current active application embed assigned to this member.
    /// </summary>
    public StrangerApplicationEmbed Application { get; private set; }

    /// <summary>
    ///     The current active newbie channel assigned to this member.
    /// </summary>
    public NewbieChannel Channel { get; private set; }

    /// <summary>
    ///     Creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Las guild join timestamp.
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Las guild leave timestamp.
    /// </summary>
    public DateTime? LeftAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     If set, the timestamp at which the member got promoted.
    /// </summary>
    public DateTime? PromotedAt { get; set; }

    /// <summary>
    ///     If set, the timestamp at which the member got kicked by moderator action.
    /// </summary>
    public DateTime? KickedAt { get; set; }

    /// <summary>
    ///     If set, the timestamp at which the member got kicked by timers.
    /// </summary>
    public DateTime? AutoKickedAt { get; set; }

    /// <summary>
    ///     If set, the timestamp at which the member got banned by moderator action.
    /// </summary>
    public DateTime? BannedAt { get; set; }

    /// <summary>
    ///     Cached member string containing username, discriminator and snowflake ID.
    /// </summary>
    public string Member { get; set; }

    /// <summary>
    ///     Cached mention string of the member.
    /// </summary>
    public string Mention { get; set; }

    /// <summary>
    ///     True if this member is no longer in the guild.
    /// </summary>
    public bool HasLeftGuild =>
        (KickedAt.HasValue && KickedAt.Value > JoinedAt) || 
        (BannedAt.HasValue && BannedAt.Value > JoinedAt) || 
        (LeftAt.HasValue && LeftAt.Value > JoinedAt);

    /// <summary>
    ///     True if member left guild by moderator action, true if left on their own.
    /// </summary>
    public bool RemovedByModeration => HasLeftGuild && (KickedAt.HasValue || BannedAt.HasValue);

    /// <summary>
    ///     MongoDB ID.
    /// </summary>
    [BsonId]
    public string ID { get; set; }
}
