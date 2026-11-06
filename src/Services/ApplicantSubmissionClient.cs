using DSharpPlus;
using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Util;

using MongoDB.Entities;

namespace IgorBot.Services;

internal sealed record ApplicantSubmission(ulong ChannelId, ulong MessageId, IReadOnlyList<OnboardingAnswer> Answers);

internal interface IApplicantSubmissionClient
{
    Task<ApplicantSubmission?> RecoverAsync(GuildMember applicant, Questionnaire questionnaire, CancellationToken ct);
    Task RefreshAsync(GuildMember applicant, ApplicantSubmission submission, OnboardingDecisionResult decision,
        OnboardingAutomationEffect effect, CancellationToken ct);
}

internal sealed class DiscordApplicantSubmissionClient(DB db, DiscordClient discord,
    ILogger<DiscordApplicantSubmissionClient> logger) : IApplicantSubmissionClient
{
    public async Task<ApplicantSubmission?> RecoverAsync(
        GuildMember applicant, Questionnaire questionnaire, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        DiscordGuild guild = discord.Guilds[applicant.GuildId];
        StrangerApplicationEmbed application = applicant.Application!;
        DiscordChannel channel = await guild.GetChannelAsync(application.SubmissionChannelId ?? questionnaire.SubmissionChannelId);
        int pages = 0;
        return await ApplicantSubmissionRecovery.RecoverAsync(applicant, questionnaire,
            async id => Snapshot(await channel.GetMessageAsync(id)),
            async before =>
            {
                ct.ThrowIfCancellationRequested();
                if (pages++ > 0) await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
                return (await channel.GetMessagesBeforeAsync(before, 100).MaterializeAsync()).Select(Snapshot).ToList();
            }, ct);
    }

    private ApplicantSubmissionMessage Snapshot(DiscordMessage message)
    {
        DiscordEmbed? embed = message.Embeds.FirstOrDefault(e => e.Title == "Questionnaire submission");
        return new ApplicantSubmissionMessage(message.Id, message.ChannelId, message.Timestamp.UtcDateTime,
            message.Author?.Id ?? 0, discord.CurrentUser?.Id ?? 0, embed?.Title, embed?.Description,
            (embed?.Fields ?? []).Select(f => new OnboardingAnswer(f.Name ?? "", f.Value ?? "")).ToArray());
    }

    public async Task RefreshAsync(GuildMember applicant, ApplicantSubmission submission,
        OnboardingDecisionResult decision, OnboardingAutomationEffect effect, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await applicant.UpdateApplicationWidget(db, discord);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not refresh screening status panel for {MemberId}", applicant.ID);
        }

        DiscordChannel channel = await discord.Guilds[applicant.GuildId].GetChannelAsync(submission.ChannelId);
        DiscordMessage message = await channel.GetMessageAsync(submission.MessageId);
        DiscordEmbed? original = message.Embeds.FirstOrDefault(e => e.Title == "Questionnaire submission");
        if (original is null)
        {
            throw new InvalidOperationException("Questionnaire submission embed is missing.");
        }

        DiscordEmbedBuilder embed = new(original);
        for (int i = embed.Fields.Count - 1; i >= 0; i--)
        {
            if (embed.Fields[i].Name == OnboardingDecisionFormatter.FieldName)
            {
                embed.RemoveFieldAt(i);
            }
        }

        embed.AddField(OnboardingDecisionFormatter.FieldName, OnboardingDecisionFormatter.Format(decision,
            willPromote: false, effect == OnboardingAutomationEffect.Promoted,
            effect == OnboardingAutomationEffect.PromotionFailed));
        await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(embed));
    }
}
