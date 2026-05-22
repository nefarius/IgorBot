namespace IgorBot.Schema;

/// <summary>
///     Immutable record of a single status transition for a guild member.
///     Stored as an embedded list on <see cref="GuildMember" />.
/// </summary>
public sealed class MemberStatusEvent
{
    /// <summary>
    ///     The status before the transition.
    /// </summary>
    public MemberStatus From { get; init; }

    /// <summary>
    ///     The status after the transition.
    /// </summary>
    public MemberStatus To { get; init; }

    /// <summary>
    ///     UTC timestamp of the transition.
    /// </summary>
    public DateTime At { get; init; } = DateTime.UtcNow;

    /// <summary>
    ///     Free-form reason string (mod display name, "migration", "honeypot", etc.).
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    ///     Discord snowflake ID of the actor who caused the transition, when known.
    /// </summary>
    public ulong? ActorId { get; init; }
}
