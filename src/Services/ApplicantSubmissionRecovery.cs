using IgorBot.Core;
using IgorBot.Schema;

namespace IgorBot.Services;

internal sealed record ApplicantSubmissionMessage(ulong Id, ulong ChannelId, DateTime Timestamp,
    ulong AuthorId, ulong BotId, string? Title, string? Description, IReadOnlyList<OnboardingAnswer> Fields);

internal static class ApplicantSubmissionRecovery
{
    public static async Task<ApplicantSubmission?> RecoverAsync(GuildMember applicant, Questionnaire questionnaire,
        Func<ulong, Task<ApplicantSubmissionMessage>> readMessage,
        Func<ulong, Task<IReadOnlyList<ApplicantSubmissionMessage>>> readPage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        StrangerApplicationEmbed application = applicant.Application!;
        bool Matches(ApplicantSubmissionMessage message) => PendingApplicantScreeningPolicy.MatchesSubmission(
            message.AuthorId, message.BotId, message.Title, message.Description, applicant.MemberId,
            application.MessageMentionUrl.ToString());
        ApplicantSubmission? Read(ApplicantSubmissionMessage message)
        {
            IReadOnlyList<OnboardingAnswer>? answers = PendingApplicantScreeningPolicy.ReadAnswers(questionnaire, message.Fields);
            return answers is null ? null : new ApplicantSubmission(message.ChannelId, message.Id, answers);
        }

        if (application.SubmissionMessageId is { } messageId)
        {
            ApplicantSubmissionMessage message = await readMessage(messageId);
            return Matches(message) ? Read(message) : null;
        }

        // Legacy records have no reference. Bound the search around the persisted submission time.
        DateTime submitted = application.QuestionnaireSubmittedAt!.Value;
        DateTime earliest = submitted.AddMinutes(-5);
        DateTime latest = submitted.AddMinutes(1);
        ulong before = (ulong)(new DateTimeOffset(DateTime.SpecifyKind(latest, DateTimeKind.Utc))
            .ToUnixTimeMilliseconds() - 1420070400000L) << 22;
        ApplicantSubmissionMessage? found = null;
        for (int page = 0; page < 100; page++)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<ApplicantSubmissionMessage> messages = await readPage(before);
            if (messages.Count == 0) return found is null ? null : Read(found);
            foreach (ApplicantSubmissionMessage message in messages.Where(m =>
                         m.Timestamp >= earliest && m.Timestamp <= latest && Matches(m)))
            {
                // An incomplete newer submission must not make an older valid one look unambiguous.
                if (found is not null) return null;
                found = message;
            }

            if (messages.Min(m => m.Timestamp) < earliest || messages.Count < 100)
                return found is null ? null : Read(found);
            ulong next = messages.Min(m => m.Id);
            if (next >= before) return null; // Malformed/non-advancing history cannot establish uniqueness.
            before = next;
        }

        return null;
    }
}
