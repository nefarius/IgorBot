namespace IgorBot.Schema;

/// <summary>
///     Describes which code path led to a <see cref="MemberStatus.LeftVoluntarily" /> classification.
/// </summary>
internal enum VoluntaryLeavePath
{
    /// <summary>
    ///     Normal case: a member who went through onboarding (or at least had a known non-legacy status) left.
    /// </summary>
    Standard,

    /// <summary>
    ///     Legacy case: the document still has <see cref="MemberStatus.Unknown" /> — existed before any
    ///     onboarding tracking was in place and was never touched by a startup migration.
    /// </summary>
    LegacyUnknown,

    /// <summary>
    ///     Pre-existing member first seen by <c>MemberDbSyncInvokable</c> and stamped
    ///     <see cref="MemberStatus.New" /> with reason <c>"discovered_by_sync"</c>, but never onboarded.
    /// </summary>
    LegacyDiscoveredBySync
}

/// <summary>
///     Pure functions for classifying a member's departure type.
///     Extracted from <c>ApplicationWorkflow.MemberRemoved</c> so the logic can be unit-tested
///     without a Discord client or a database connection.
/// </summary>
internal static class MemberLifecycleClassifier
{
    /// <summary>
    ///     Reason string written to <see cref="MemberStatusEvent.Reason" /> and
    ///     <see cref="GuildMember.StatusReason" /> when a document is first inserted by
    ///     <c>MemberDbSyncInvokable</c>. Used as a sentinel by
    ///     <see cref="ClassifyVoluntaryLeavePath" /> to identify pre-existing members
    ///     who were never onboarded before leaving.
    /// </summary>
    internal const string DiscoveredBySyncReason = "discovered_by_sync";

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

    /// <summary>
    ///     Classifies *how* a voluntary leave came about so callers can attach a structured reason and
    ///     emit a distinct log line for legacy/untracked cases.
    /// </summary>
    public static VoluntaryLeavePath ClassifyVoluntaryLeavePath(GuildMember member)
    {
        // Status was never resolved — document predates all onboarding tracking.
        if (member.Status == MemberStatus.Unknown
            && member.StatusHistory.Count == 0
            && member.Application is null
            && member.Channel is null)
        {
            return VoluntaryLeavePath.LegacyUnknown;
        }

        // Document was inserted by MemberDbSyncInvokable (stamped New with a single
        // "discovered_by_sync" history entry) but the member left before ever being onboarded.
        if (member.Status == MemberStatus.New
            && member.Application is null
            && member.Channel is null
            && member.StatusHistory.Count == 1
            && member.StatusHistory[0].Reason == DiscoveredBySyncReason)
        {
            return VoluntaryLeavePath.LegacyDiscoveredBySync;
        }

        return VoluntaryLeavePath.Standard;
    }
}
