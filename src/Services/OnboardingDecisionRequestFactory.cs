using OllamaSharp.Models;

namespace IgorBot.Services;

internal static class OnboardingDecisionRequestFactory
{
    public static SystemOneRequest Create(OnboardingSubmission submission, string model)
    {
        string context = string.IsNullOrWhiteSpace(submission.Context)
            ? submission.Description
            : submission.Context.Trim();

        return new SystemOneRequest
        {
            Model = model,
            State = new
            {
                questionnaire = submission.QuestionnaireName,
                description = submission.Description,
                context,
                answers = submission.Answers.Select(answer => new
                {
                    question = answer.Question,
                    answer = answer.Answer
                }).ToArray()
            },
            Questions = new Dictionary<string, SystemOneQuestion>
            {
                [OnboardingDecisionLabels.QuestionId] = new SystemOneChoiceQuestion
                {
                    Instructions = $"Admission decision for a Discord application. {context}",
                    Criteria = new Dictionary<string, string?>
                    {
                        [OnboardingDecisionLabels.Normal] =
                            "Accept the rules and give any good-faith reason: the project, bugs, help, updates, or knowing the owner.",
                        [OnboardingDecisionLabels.Bot] =
                            "Spam, advertising, a link, an airdrop, or a generic template not about this server.",
                        [OnboardingDecisionLabels.Reject] =
                            "Joke, insult, idk, refused the rules, or no real reason."
                    }
                }
            }
        };
    }
}
