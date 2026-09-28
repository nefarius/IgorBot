using DSharpPlus.Exceptions;

using IgorBot.Schema;

namespace IgorBot.Services;

/// <summary>
///     Pure eligibility result for a honeypot post.
/// </summary>
internal enum HoneypotEligibility
{
    Enforce,
    SkipBot,
    SkipOwner,
    SkipExcludedRole
}

/// <summary>
///     Pure honeypot decision helpers shared by the live event path and reconciliation.
/// </summary>
internal static class HoneypotRules
{
    /// <summary>
    ///     Classifies whether a poster should be banned for writing in the honeypot channel.
    /// </summary>
    public static HoneypotEligibility Evaluate(
        bool isBot,
        bool isOwner,
        IEnumerable<ulong> roleIds,
        IReadOnlyCollection<ulong> exclusionRoleIds)
    {
        if (isBot)
        {
            return HoneypotEligibility.SkipBot;
        }

        if (isOwner)
        {
            return HoneypotEligibility.SkipOwner;
        }

        if (roleIds.Any(exclusionRoleIds.Contains))
        {
            return HoneypotEligibility.SkipExcludedRole;
        }

        return HoneypotEligibility.Enforce;
    }

    /// <summary>
    ///     Returns <see langword="true" /> when the member is already in a terminal Discord ban state.
    /// </summary>
    public static bool IsTerminalBan(MemberStatus status) =>
        status is MemberStatus.BannedByHoneypot
            or MemberStatus.BannedByModerator
            or MemberStatus.BannedExternally;

    /// <summary>
    ///     Returns <see langword="true" /> when another ban path already handled this member
    ///     and reconciliation should not issue a second Discord ban.
    /// </summary>
    public static bool ShouldSkipDiscordBan(MemberStatus status) =>
        status is MemberStatus.BannedByModerator or MemberStatus.BannedExternally;

    /// <summary>
    ///     403 (missing permission / hierarchy) and 404 (unknown user) will not succeed on retry.
    /// </summary>
    public static bool IsPermanentBanFailure(Exception ex) =>
        ex is UnauthorizedException or NotFoundException;
}
