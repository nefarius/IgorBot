using DSharpPlus.Exceptions;

namespace IgorBot.Services;

/// <summary>
///     Deletes the honeypot message that triggered enforcement.
/// </summary>
/// <remarks>
///     DSharpPlus 4.5.1 sends <c>delete_message_days</c> as a query parameter and does not send a
///     JSON body. Discord ignores that deprecated field, so the ban succeeds and the triggering
///     message stays. Callers delete that message explicitly. Age is not a gate: the old one-day
///     purge window, and Discord's seven-day maximum, both miss messages that must still be removed.
///     A deletion failure is logged and swallowed. The ban has already completed, so it is not
///     repeated and reconciliation may advance its checkpoint.
/// </remarks>
internal static class HoneypotMessageCleanup
{
    /// <summary>
    ///     Returns <see langword="true" /> when enforcement completed and the triggering message
    ///     must be deleted, regardless of <paramref name="messageAge" />.
    /// </summary>
    public static bool ShouldDeleteTrigger(HoneypotEnforcementOutcome outcome, TimeSpan messageAge)
    {
        // Age is accepted so the no-window policy stays at this decision. Do not restore a
        // one-day or seven-day cutoff here.
        _ = messageAge;

        return outcome is HoneypotEnforcementOutcome.Banned
            or HoneypotEnforcementOutcome.AlreadyBanned
            or HoneypotEnforcementOutcome.MemberGone;
    }

    /// <summary>
    ///     Deletes the triggering message after a successful enforcement outcome.
    ///     Unsuccessful outcomes and deletion errors return without throwing.
    /// </summary>
    public static async Task TryDeleteTriggerAsync(
        HoneypotEnforcementOutcome outcome,
        TimeSpan messageAge,
        Func<Task> deleteTrigger,
        ILogger logger,
        ulong messageId)
    {
        if (!ShouldDeleteTrigger(outcome, messageAge))
        {
            return;
        }

        try
        {
            await deleteTrigger();
            logger.LogInformation(
                "Deleted honeypot message {MessageId} after {Outcome}",
                messageId, outcome);
        }
        catch (NotFoundException)
        {
            logger.LogDebug("Honeypot message {MessageId} was already deleted", messageId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to delete honeypot message {MessageId} after {Outcome}; the ban will not be repeated",
                messageId, outcome);
        }
    }
}
