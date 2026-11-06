using IgorBot.Core;
using IgorBot.Schema;

namespace IgorBot.Services;

internal static class PendingApplicantScreeningPolicy
{
    public static bool NeedsScreening(MemberStatus status, StrangerApplicationEmbed? application) =>
        status == MemberStatus.QuestionnaireSubmitted && application?.QuestionnaireSubmittedAt is not null &&
        application.Decision is not { AutomationAttempted: true } &&
        (application.Decision is null || application.Decision.Outcome is "Disabled" or "Unavailable");

    public static bool MatchesSubmission(ulong authorId, ulong botId, string? title, string? description,
        ulong memberId, string panelUrl) =>
        botId != 0 && authorId == botId && title == "Questionnaire submission" && description is not null &&
        (description.Contains($"<@{memberId}>", StringComparison.Ordinal) ||
         description.Contains($"<@!{memberId}>", StringComparison.Ordinal)) &&
        description.EndsWith($"Status panel: {panelUrl}", StringComparison.Ordinal);

    public static IReadOnlyList<OnboardingAnswer>? ReadAnswers(
        Questionnaire questionnaire, IEnumerable<OnboardingAnswer> fields)
    {
        List<OnboardingAnswer> answers = fields.Where(f => f.Question != OnboardingDecisionFormatter.FieldName).ToList();
        if (answers.Count == 0 || answers.Count != questionnaire.Questions.Count ||
            !answers.Select(a => a.Question).SequenceEqual(questionnaire.Questions.Select(q => q.Content)))
        {
            return null;
        }

        return answers;
    }
}
