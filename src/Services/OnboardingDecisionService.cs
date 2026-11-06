using IgorBot.Core;

namespace IgorBot.Services;

internal sealed class OnboardingDecisionService(
    SystemOneSettings settings,
    IOnboardingDecisionClient client,
    ILogger<OnboardingDecisionService> logger) : IOnboardingDecisionService
{
    public async Task<OnboardingDecisionResult> EvaluateAsync(
        Questionnaire questionnaire,
        IReadOnlyList<OnboardingAnswer> answers,
        ulong guildId,
        ulong memberId,
        CancellationToken cancellationToken = default)
    {
        DateTime evaluatedAt = DateTime.UtcNow;
        QuestionnaireAutomation automation = questionnaire.Automation;
        if (!automation.Enabled)
        {
            logger.LogInformation("System One skipped guild {GuildId} member {MemberId}: questionnaire screening disabled", guildId, memberId);
            return OnboardingDecisionPolicy.Disabled(evaluatedAt);
        }

        if (!settings.IsConfigured)
        {
            logger.LogWarning("System One skipped guild {GuildId} member {MemberId}: endpoint not configured", guildId, memberId);
            return OnboardingDecisionPolicy.Unavailable(evaluatedAt, "System One endpoint is not configured.");
        }

        try
        {
            SystemOneClassification classification = await client.ClassifyAsync(
                new OnboardingSubmission
                {
                    QuestionnaireName = questionnaire.Name,
                    Description = questionnaire.Description,
                    Context = automation.Context,
                    Answers = answers
                },
                cancellationToken);

            OnboardingDecisionResult result = OnboardingDecisionPolicy.FromClassification(
                classification,
                automation.AutoPromoteNormalProbability,
                evaluatedAt);

            logger.LogInformation(
                "System One screened guild {GuildId} member {MemberId}: {Outcome} label {Label}",
                guildId,
                memberId,
                result.Outcome,
                result.Label);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "System One screening failed for guild {GuildId} member {MemberId}",
                guildId,
                memberId);
            return OnboardingDecisionPolicy.Unavailable(
                evaluatedAt,
                $"System One request failed ({ex.GetType().Name}).");
        }
    }
}
