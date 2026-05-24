#nullable enable

using System.Diagnostics.CodeAnalysis;

using MongoDB.Bson.Serialization.Attributes;

namespace IgorBot.Schema;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
internal sealed partial class GuildMember
{
    /// <summary>
    ///     Discord user/member ID.
    /// </summary>
    public required ulong MemberId { get; init; }

    /// <summary>
    ///     Guild ID.
    /// </summary>
    public required ulong GuildId { get; init; }

    /// <summary>
    ///     The current active application embed assigned to this member.
    /// </summary>
    public StrangerApplicationEmbed? Application { get; private set; }

    /// <summary>
    ///     The current active newbie channel assigned to this member.
    /// </summary>
    public NewbieChannel? Channel { get; private set; }

    /// <summary>
    ///     Creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>
    ///     Last guild join timestamp.
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Last guild leave timestamp.
    /// </summary>
    public DateTime? LeftAt { get; set; }

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
    ///     If set, the timestamp at which the stranger role got removed from the member.
    /// </summary>
    public DateTime? StrangerRoleRemovedAt { get; set; }

    /// <summary>
    ///     If set, the timestamp at which the full member role got added to the member.
    /// </summary>
    public DateTime? FullMemberAt { get; set; }

    /// <summary>
    ///     Cached member string containing username, discriminator and snowflake ID.
    /// </summary>
    public required string Member { get; set; }

    /// <summary>
    ///     Cached mention string of the member.
    /// </summary>
    public required string Mention { get; set; }

    /// <summary>
    ///     True if the member is freshly joined (no exit event of any kind has occurred).
    ///     When <see cref="Status" /> is known, delegates to it; falls back to legacy timestamps
    ///     for documents not yet migrated (<see cref="MemberStatus.Unknown" />).
    /// </summary>
    public bool IsNew => Status != MemberStatus.Unknown
        ? Status == MemberStatus.New || Status == MemberStatus.Onboarding ||
          Status == MemberStatus.QuestionnaireSubmitted
        : !PromotedAt.HasValue &&
          !KickedAt.HasValue &&
          !AutoKickedAt.HasValue &&
          !BannedAt.HasValue &&
          !LeftAt.HasValue;

    /// <summary>
    ///     True if this member is no longer in the guild.
    ///     When <see cref="Status" /> is known, delegates to it; falls back to legacy timestamps.
    /// </summary>
    public bool HasLeftGuild => Status != MemberStatus.Unknown
        ? Status is MemberStatus.LeftVoluntarily or MemberStatus.KickedByModerator
              or MemberStatus.BannedByModerator or MemberStatus.AutoKicked
              or MemberStatus.BannedByHoneypot or MemberStatus.BannedExternally
              or MemberStatus.KickedExternally
        : (KickedAt.HasValue && KickedAt.Value >= JoinedAt) ||
          (BannedAt.HasValue && BannedAt.Value >= JoinedAt) ||
          (AutoKickedAt.HasValue && AutoKickedAt.Value >= JoinedAt) ||
          (LeftAt.HasValue && LeftAt.Value >= JoinedAt);

    /// <summary>
    ///     True if this member is currently present in the guild.
    /// </summary>
    public bool IsInGuild => !HasLeftGuild;

    /// <summary>
    ///     True if the member is in the guild and has full member role.
    /// </summary>
    public bool IsFullMember => Status != MemberStatus.Unknown
        ? Status == MemberStatus.FullMember
        : IsInGuild && PromotedAt.HasValue;

    /// <summary>
    ///     True if the member is currently banned.
    /// </summary>
    public bool IsBanned => Status != MemberStatus.Unknown
        ? Status is MemberStatus.BannedByModerator or MemberStatus.BannedByHoneypot
              or MemberStatus.BannedExternally
        : BannedAt.HasValue;

    /// <summary>
    ///     True if the member left the guild as the result of a moderator action.
    /// </summary>
    public bool RemovedByModeration { get; set; }

    /// <summary>
    ///     True if newbie workflow is starting, false otherwise.
    /// </summary>
    public bool IsOnboardingInProgress { get; set; }

    // ─── Phase 2 canonical state ─────────────────────────────────────────────

    /// <summary>
    ///     Canonical lifecycle state. Set via <see cref="GuildMember.TransitionToAsync" />.
    /// </summary>
    public MemberStatus Status { get; set; } = MemberStatus.Unknown;

    /// <summary>
    ///     UTC timestamp of the most recent status transition.
    /// </summary>
    public DateTime? StatusChangedAt { get; set; }

    /// <summary>
    ///     Free-form reason attached to the most recent status transition.
    /// </summary>
    public string? StatusReason { get; set; }

    /// <summary>
    ///     Ordered list of every status transition this member has gone through.
    /// </summary>
    public List<MemberStatusEvent> StatusHistory { get; set; } = new();

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     MongoDB ID.
    /// </summary>
    [BsonId]
    public string ID { get; set; } = null!;
}