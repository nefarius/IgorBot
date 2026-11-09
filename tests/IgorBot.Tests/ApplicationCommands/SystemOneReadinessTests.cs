using DSharpPlus.Entities;

using FluentAssertions;

using IgorBot.ApplicationCommands;
using IgorBot.Core;
using IgorBot.Services;

namespace IgorBot.Tests.ApplicationCommands;

public sealed class SystemOneReadinessTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ReadinessFailures_AreReportedIndependently(bool promotionFails, bool channelFails)
    {
        DiscordEmbedBuilder embed = new();
        int promotionCalls = 0;
        int channelCalls = 0;
        List<Exception> failures = [];
        await SystemOneCommands.AddReadinessChecksAsync(embed,
            () =>
            {
                promotionCalls++;
                if (promotionFails) throw new InvalidOperationException("Missing role");
                return Task.FromResult<string?>(null);
            },
            () =>
            {
                channelCalls++;
                if (channelFails) throw new UnauthorizedAccessException("No channel access");
                return Task.FromResult("Ready");
            }, failures.Add);

        promotionCalls.Should().Be(1);
        channelCalls.Should().Be(1);
        embed.Fields.Should().HaveCount(2);
        embed.Fields.Single(f => f.Name == "Promotion readiness").Value.Should()
            .Be(promotionFails ? "Could not verify (InvalidOperationException). Check the bot log." : "Ready");
        embed.Fields.Single(f => f.Name == "Submission channel access").Value.Should()
            .Be(channelFails ? "Could not verify (UnauthorizedAccessException). Check the bot log." : "Ready");
        failures.Should().HaveCount((promotionFails ? 1 : 0) + (channelFails ? 1 : 0));
    }

    [Fact]
    public void SimulationSubmission_UsesCurrentQuestionnaireContext()
    {
        Questionnaire questionnaire = new()
        {
            Id = "newbie",
            Name = "On-boarding",
            Description = "Current description",
            Automation = new QuestionnaireAutomation
            {
                Enabled = false,
                Context = "  Technical project sharing is legitimate.  ",
                AutoPromoteNormalProbability = 0.91
            }
        };

        OnboardingSubmission submission = SystemOneSimulation.CreateSubmission(
            questionnaire, "  I share an open-source controller project.  ");

        submission.QuestionnaireName.Should().Be("On-boarding");
        submission.Description.Should().Be("Current description");
        submission.Context.Should().Be("  Technical project sharing is legitimate.  ");
        submission.Answers.Should().ContainSingle()
            .Which.Should().Be(new OnboardingAnswer(
                "Synthetic application", "I share an open-source controller project."));
    }

    [Theory]
    [InlineData(OnboardingDecisionOutcome.AutoPromote, "automatic promotion")]
    [InlineData(OnboardingDecisionOutcome.ManualReview, "manual review")]
    internal void SimulationResult_ReportsProductionActionWithoutSideEffects(
        OnboardingDecisionOutcome outcome,
        string action)
    {
        DiscordEmbed embed = SystemOneSimulation.Format(new SystemOneProbeReport
        {
            Status = SystemOneProbeStatus.Succeeded,
            Model = "d1-3B-Q8_0",
            Label = OnboardingDecisionLabels.Normal,
            Probabilities = new Dictionary<string, double> { [OnboardingDecisionLabels.Normal] = 0.67 },
            Confidence = 0.5,
            Threshold = 0.8,
            Outcome = outcome
        }, screeningEnabled: true);

        embed.Description.Should().Contain("No member, application, database record, or role was changed.");
        embed.Fields!.Single(field => field.Name == "Action").Value.Should().Be(action);
        embed.Fields!.Single(field => field.Name == "Threshold").Value.Should().Be("80 %");
    }

    [Fact]
    internal void DisabledScreening_QualifiesAutomaticPromotionWithoutChangingClassification()
    {
        DiscordEmbed embed = SystemOneSimulation.Format(new SystemOneProbeReport
        {
            Status = SystemOneProbeStatus.Succeeded,
            Label = OnboardingDecisionLabels.Normal,
            Outcome = OnboardingDecisionOutcome.AutoPromote
        }, screeningEnabled: false);

        embed.Fields!.Single(field => field.Name == "Recommendation").Value.Should().Be(OnboardingDecisionLabels.Normal);
        embed.Fields!.Single(field => field.Name == "Action").Value.Should()
            .Be("screening is disabled; automatic promotion if screening is enabled");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SimulationSubmission_RejectsAnEmptyApplication(string application)
    {
        Action create = () => SystemOneSimulation.CreateSubmission(Questionnaire(), application);

        create.Should().Throw<ArgumentException>().WithMessage("Provide the synthetic application.*");
    }

    [Fact]
    public void SimulationSubmission_RejectsAnApplicationBeyondTheDiscordOptionLimit()
    {
        string application = new('a', SystemOneSimulation.MaximumApplicationLength + 1);

        Action create = () => SystemOneSimulation.CreateSubmission(Questionnaire(), application);

        create.Should().Throw<ArgumentException>()
            .WithMessage("*6000 characters or fewer*");
    }

    [Fact]
    public void SimulationFailure_KeepsTheDeferredResponseExplicit()
    {
        DiscordEmbed embed = SystemOneSimulation.Failure(
            "The simulation could not be completed (InvalidOperationException). No membership data was changed.");

        embed.Description.Should().Contain("InvalidOperationException");
        embed.Description.Should().Contain("No membership data was changed.");
    }

    private static Questionnaire Questionnaire() => new()
    {
        Id = "newbie",
        Name = "On-boarding",
        Description = "Current description",
        Automation = new QuestionnaireAutomation { Context = "Technical community" }
    };
}
