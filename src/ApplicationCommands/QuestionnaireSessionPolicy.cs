namespace IgorBot.ApplicationCommands;

public enum QuestionnaireReplyDisposition
{
    Answer,
    TimedOut,
    Cancelled
}

/// <summary>
///     Decisions for the <c>/apply member</c> questionnaire that do not need a Discord client.
/// </summary>
internal static class QuestionnaireSessionPolicy
{
    public static QuestionnaireReplyDisposition Classify(bool timedOut, bool cancelled) =>
        cancelled
            ? QuestionnaireReplyDisposition.Cancelled
            : timedOut
                ? QuestionnaireReplyDisposition.TimedOut
                : QuestionnaireReplyDisposition.Answer;

    public static bool ShouldRearmAutoKick(QuestionnaireReplyDisposition disposition) =>
        disposition is QuestionnaireReplyDisposition.TimedOut or QuestionnaireReplyDisposition.Cancelled;

    public static bool ShouldRearmAutoKickAfterDmFailure() => true;

    public static bool ShouldDisableAutoKickWhileAnswering() => true;

    public static bool HasStrangerRole(IEnumerable<ulong> roleIds, ulong strangerRoleId) =>
        roleIds.Contains(strangerRoleId);

    public static bool IsBlockedChannel(IEnumerable<ulong> blockedChannelIds, ulong channelId) =>
        blockedChannelIds.Contains(channelId);

    public static TimeSpan ReplyTimeout(ulong timeoutMinutes) => TimeSpan.FromMinutes(timeoutMinutes);

    public static string DisplayAnswer(string? content) =>
        string.IsNullOrEmpty(content) ? "<no value>" : content;
}
