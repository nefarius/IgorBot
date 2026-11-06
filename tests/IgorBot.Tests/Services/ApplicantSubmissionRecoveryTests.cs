using FluentAssertions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;

namespace IgorBot.Tests.Services;

public sealed class ApplicantSubmissionRecoveryTests
{
    private static readonly DateTime Submitted = new(2026, 11, 6, 16, 46, 55, DateTimeKind.Utc);
    private static readonly Questionnaire Questionnaire = new()
    {
        Name = "On-boarding", Id = "member", Questions = [new Question { Content = "Rules?" }]
    };

    [Fact]
    public async Task DirectReference_UsesExactMessageAndDoesNotScanHistory()
    {
        GuildMember applicant = Applicant();
        applicant.Application!.SubmissionMessageId = 123;
        ApplicantSubmission? result = await ApplicantSubmissionRecovery.RecoverAsync(applicant, Questionnaire,
            id => { id.Should().Be(123); return Task.FromResult(Message(123)); },
            _ => throw new InvalidOperationException("History must not be scanned"), CancellationToken.None);
        result!.MessageId.Should().Be(123);
        result.Answers.Should().Equal(new OnboardingAnswer("Rules?", "Yup"));
    }

    [Fact]
    public async Task LegacySubmission_IsRecoveredAcrossPagesWithMonotonicCursor()
    {
        GuildMember applicant = Applicant();
        int pages = 0;
        ulong previous = ulong.MaxValue;
        ApplicantSubmission? result = await ApplicantSubmissionRecovery.RecoverAsync(applicant, Questionnaire,
            _ => throw new InvalidOperationException("No message reference"), before =>
            {
                before.Should().BeLessThan(previous);
                previous = before;
                IReadOnlyList<ApplicantSubmissionMessage> page = pages++ == 0
                    ? Enumerable.Range(1, 100).Select(i => Message((ulong)(1000 + i)) with { AuthorId = 88 }).ToArray()
                    : [Message(999), Message(998) with { Timestamp = Submitted.AddMinutes(-10), AuthorId = 88 }];
                return Task.FromResult(page);
            }, CancellationToken.None);
        result!.MessageId.Should().Be(999);
        pages.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleMatchingSubmissions_AreAmbiguousEvenIfOneIsIncomplete(bool incomplete)
    {
        ApplicantSubmissionMessage newer = incomplete ? Message(124) with { Fields = [] } : Message(124);
        (await RecoverPageAsync([newer, Message(123)])).Should().BeNull();
    }

    [Fact]
    public async Task WrongAuthorWrongMemberOldPanelOldTimestampOrChangedQuestions_AreNotRecovered()
    {
        ApplicantSubmissionMessage valid = Message(123);
        foreach (ApplicantSubmissionMessage invalid in new[]
                 {
                     valid with { AuthorId = 88 }, valid with { BotId = 0 },
                     valid with { Description = "Author: (<@43>), Status panel: https://discord.com/channels/1/2/3" },
                     valid with { Description = "Author: (<@42>), Status panel: https://discord.com/channels/1/2/30" },
                     valid with { Timestamp = Submitted.AddMinutes(-10) },
                     valid with { Fields = [new OnboardingAnswer("Different question", "Yup")] }
                 })
        {
            (await RecoverPageAsync([invalid])).Should().BeNull();
        }
    }

    [Fact]
    public async Task CancelledScan_DoesNotReadHistory()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Func<Task> run = () => ApplicantSubmissionRecovery.RecoverAsync(Applicant(), Questionnaire,
            _ => throw new InvalidOperationException("No read"), _ => throw new InvalidOperationException("No read"), cancellation.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    private static Task<ApplicantSubmission?> RecoverPageAsync(IReadOnlyList<ApplicantSubmissionMessage> messages) =>
        ApplicantSubmissionRecovery.RecoverAsync(Applicant(), Questionnaire,
            _ => throw new InvalidOperationException("No reference"), _ => Task.FromResult(messages), CancellationToken.None);

    private static ApplicantSubmissionMessage Message(ulong id) => new(id, 7, Submitted.AddSeconds(-1), 9, 9,
        "Questionnaire submission", "Author: (<@42>), Status panel: https://discord.com/channels/1/2/3",
        [new OnboardingAnswer("Rules?", "Yup")]);

    private static GuildMember Applicant()
    {
        GuildMember member = new() { ID = "1-42", GuildId = 1, MemberId = 42, Member = "applicant", Mention = "<@42>" };
        typeof(GuildMember).GetProperty(nameof(GuildMember.Application))!.SetValue(member,
            new StrangerApplicationEmbed { ID = "1-2-3", GuildId = 1, ChannelId = 2, MessageId = 3, QuestionnaireSubmittedAt = Submitted });
        return member;
    }
}
