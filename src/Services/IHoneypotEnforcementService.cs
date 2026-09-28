namespace IgorBot.Services;

/// <summary>
///     Outcome of a honeypot enforcement attempt.
/// </summary>
internal enum HoneypotEnforcementOutcome
{
    Banned,
    AlreadyBanned,
    MemberGone,
    Failed
}

/// <summary>
///     Shared DB-first honeypot ban used by live <c>MESSAGE_CREATE</c> handling and history reconciliation.
/// </summary>
internal interface IHoneypotEnforcementService
{
    /// <summary>
    ///     Marks the member as banned by honeypot, then issues the Discord ban.
    ///     Concurrent calls for the same guild member are serialized.
    /// </summary>
    Task<HoneypotEnforcementOutcome> EnforceAsync(
        ulong guildId,
        ulong memberId,
        string memberDisplay,
        string mention);
}
