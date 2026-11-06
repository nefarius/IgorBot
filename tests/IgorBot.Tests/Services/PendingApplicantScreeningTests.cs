using FluentAssertions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;

namespace IgorBot.Tests.Services;

public sealed class PendingApplicantScreeningTests
{
    [Theory]
    [InlineData(null, false, true)]
    [InlineData("Disabled", false, true)]
    [InlineData("Unavailable", false, true)]
    [InlineData("ManualReview", false, false)]
    [InlineData("AutoPromote", false, false)]
    [InlineData("Unavailable", true, false)]
    public void NeedsScreening_OnlyRetriesUnscreenedOrUnavailableApplications(
        string? outcome, bool attempted, bool expected)
    {
        StrangerApplicationEmbed application = Application();
        application.Decision = outcome is null ? null : new OnboardingDecisionRecord
        {
            Outcome = outcome, AutomationAttempted = attempted
        };

        PendingApplicantScreeningPolicy.NeedsScreening(MemberStatus.QuestionnaireSubmitted, application)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(MemberStatus.New)]
    [InlineData(MemberStatus.Onboarding)]
    [InlineData(MemberStatus.FullMember)]
    [InlineData(MemberStatus.LeftVoluntarily)]
    [InlineData(MemberStatus.KickedByModerator)]
    [InlineData(MemberStatus.BannedByModerator)]
    [InlineData(MemberStatus.StrangerRoleRemoved)]
    public void NeedsScreening_DoesNotProcessOtherLifecycleStates(MemberStatus status)
    {
        PendingApplicantScreeningPolicy.NeedsScreening(status, Application()).Should().BeFalse();
    }

    [Fact]
    public void NeedsScreening_RequiresSubmissionAndApplication()
    {
        PendingApplicantScreeningPolicy.NeedsScreening(MemberStatus.QuestionnaireSubmitted, null).Should().BeFalse();
        StrangerApplicationEmbed application = Application();
        application.QuestionnaireSubmittedAt = null;
        PendingApplicantScreeningPolicy.NeedsScreening(MemberStatus.QuestionnaireSubmitted, application).Should().BeFalse();
    }

    [Fact]
    public void MatchesSubmission_RequiresBotAuthorMemberAndExactApplicationPanel()
    {
        const string panel = "https://discord.com/channels/1/2/3";
        const string description = "Questionnaire: **On-boarding**, Author: user (<@42>), Status panel: " + panel;
        PendingApplicantScreeningPolicy.MatchesSubmission(9, 9, "Questionnaire submission", description, 42, panel)
            .Should().BeTrue();
        PendingApplicantScreeningPolicy.MatchesSubmission(8, 9, "Questionnaire submission", description, 42, panel)
            .Should().BeFalse();
        PendingApplicantScreeningPolicy.MatchesSubmission(9, 9, "Questionnaire submission", description, 43, panel)
            .Should().BeFalse();
        PendingApplicantScreeningPolicy.MatchesSubmission(9, 9, "Questionnaire submission", description, 42, panel + "4")
            .Should().BeFalse();
        PendingApplicantScreeningPolicy.MatchesSubmission(9, 9, "Other embed", description, 42, panel)
            .Should().BeFalse();
    }

    [Fact]
    public void ReadAnswers_RejectsMissingReorderedOrChangedQuestionsAndExcludesScreening()
    {
        Questionnaire questionnaire = new()
        {
            Id = "member", Name = "On-boarding",
            Questions = [new Question { Content = "Rules?" }, new Question { Content = "Why join?" }]
        };
        OnboardingAnswer[] answers = [new("Rules?", "Yes"), new("Why join?", "Report a bug")];
        PendingApplicantScreeningPolicy.ReadAnswers(questionnaire,
                [.. answers, new(OnboardingDecisionFormatter.FieldName, "Classifier unavailable.")])
            .Should().Equal(answers);
        PendingApplicantScreeningPolicy.ReadAnswers(questionnaire, answers.Take(1)).Should().BeNull();
        PendingApplicantScreeningPolicy.ReadAnswers(questionnaire, answers.Reverse()).Should().BeNull();
        questionnaire.Questions[0].Content = "New question";
        PendingApplicantScreeningPolicy.ReadAnswers(questionnaire, answers).Should().BeNull();
    }

    private static StrangerApplicationEmbed Application() => new()
    {
        GuildId = 1, ChannelId = 2, MessageId = 3, ID = "1-2-3",
        QuestionnaireSubmittedAt = DateTime.UtcNow
    };
}
