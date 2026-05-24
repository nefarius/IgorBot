namespace IgorBot.Schema;

/// <summary>
///     Pure functions for classifying a member's departure type.
///     Extracted from <c>ApplicationWorkflow.MemberRemoved</c> so the logic can be unit-tested
///     without a Discord client or a database connection.
/// </summary>
internal static class MemberLifecycleClassifier
{
    /// <summary>
    ///     Returns <see langword="true" /> when a member's departure should be recorded as a voluntary leave.
    ///     Migrated documents (<see cref="MemberStatus" /> != <see cref="MemberStatus.Unknown" />) are eligible
    ///     when in a non-terminal active state.
    ///     Un-migrated documents (<see cref="MemberStatus.Unknown" />) are eligible only when no legacy terminal
    ///     timestamp (<c>KickedAt</c>, <c>BannedAt</c>, <c>AutoKickedAt</c>) has already been set.
    /// </summary>
    public static bool IsEligibleForVoluntaryLeave(GuildMember member) =>
        member.Status switch
        {
            MemberStatus.New or
            MemberStatus.Onboarding or
            MemberStatus.QuestionnaireSubmitted or
            MemberStatus.FullMember or
            MemberStatus.StrangerRoleRemoved => true,

            // Legacy document: defer to timestamp fields to avoid overwriting a terminal marker.
            MemberStatus.Unknown =>
                !member.KickedAt.HasValue &&
                !member.BannedAt.HasValue &&
                !member.AutoKickedAt.HasValue,

            // Already in a terminal state set by a prior action — do not overwrite.
            _ => false
        };
}
