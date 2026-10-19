namespace IgorBot.Schema;

/// <summary>
///     Which current statuses a conditional (compare-and-set) transition may start from. Used by the
///     <c>TryTransitionAsync</c> write path so a slow handler holding a stale snapshot can never overwrite a newer,
///     more specific status.
/// </summary>
internal static class MemberTransitionPolicy
{
    /// <summary>
    ///     Non-terminal states in which the member has not (yet) been promoted.
    /// </summary>
    private static readonly MemberStatus[] Pending =
    [
        MemberStatus.Unknown,
        MemberStatus.New,
        MemberStatus.Onboarding,
        MemberStatus.QuestionnaireSubmitted,
        MemberStatus.StrangerRoleRemoved
    ];

    /// <summary>
    ///     Every state in which the member is still present in the guild.
    /// </summary>
    private static readonly MemberStatus[] Present = [.. Pending, MemberStatus.FullMember];

    /// <summary>
    ///     Returns the statuses <paramref name="next" /> may be entered from, or <see langword="null" /> when the
    ///     target is not restricted by policy.
    /// </summary>
    public static IReadOnlyCollection<MemberStatus>? AllowedFrom(MemberStatus next) =>
        next switch
        {
            // Onboarding setup only makes sense for members that are not yet in (or past) the flow.
            MemberStatus.Onboarding =>
                [MemberStatus.Unknown, MemberStatus.New, MemberStatus.StrangerRoleRemoved],

            // A questionnaire can only be submitted against an active application.
            MemberStatus.QuestionnaireSubmitted =>
                [MemberStatus.Unknown, MemberStatus.Onboarding],

            // Promotion must never resurrect departed members.
            MemberStatus.FullMember => Pending,

            // Removing the stranger role must never downgrade a promoted member or a departed one.
            MemberStatus.StrangerRoleRemoved =>
                [MemberStatus.Unknown, MemberStatus.New, MemberStatus.Onboarding, MemberStatus.QuestionnaireSubmitted],

            // Only the idle-kick candidate state can be auto-kicked.
            MemberStatus.AutoKicked => [MemberStatus.Unknown, MemberStatus.Onboarding],

            MemberStatus.LeftVoluntarily or
                MemberStatus.KickedByModerator or
                MemberStatus.BannedByModerator or
                MemberStatus.KickedExternally => Present,

            _ => null
        };

    /// <summary>
    ///     A departure that was recorded as voluntary may be upgraded to an external kick when the audit-log entry
    ///     arrives shortly after the removal event.
    /// </summary>
    public static readonly TimeSpan ExternalKickUpgradeWindow = TimeSpan.FromSeconds(30);

    public static bool CanUpgradeLeaveToExternalKick(MemberStatus status, DateTime? changedAt, DateTime now) =>
        status == MemberStatus.LeftVoluntarily
        && changedAt.HasValue
        && now - changedAt.Value <= ExternalKickUpgradeWindow;
}
