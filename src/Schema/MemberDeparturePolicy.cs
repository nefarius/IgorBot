namespace IgorBot.Schema;

/// <summary>
///     Timing and reason selection for a member-removed event that races with a kick or ban handler.
/// </summary>
internal static class MemberDeparturePolicy
{
    /// <summary>
    ///     How long to wait for a concurrent terminal-status write before classifying a leave as voluntary.
    /// </summary>
    public static readonly TimeSpan ConcurrentTerminalStatusGrace = TimeSpan.FromMilliseconds(500);

    public static bool ShouldWaitForConcurrentTerminalWrite(GuildMember member) =>
        MemberLifecycleClassifier.IsEligibleForVoluntaryLeave(member);

    public static string ReasonFor(VoluntaryLeavePath path) =>
        path switch
        {
            VoluntaryLeavePath.LegacyUnknown => "legacy_unknown_voluntary_leave",
            VoluntaryLeavePath.LegacyDiscoveredBySync => "legacy_discovered_voluntary_leave",
            _ => "voluntary_leave"
        };
}
