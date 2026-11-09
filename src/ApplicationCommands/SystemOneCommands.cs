using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using DSharpPlus.Commands;
using DSharpPlus.Commands.ArgumentModifiers;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

using IgorBot.Services;
using IgorBot.Core;
using IgorBot.Util;

namespace IgorBot.ApplicationCommands;

[Command("systemone"), Description("Check the System One decision endpoint. Administrator only.")]
[RequirePermissions(botPermissions: [], userPermissions: [DiscordPermission.Administrator])]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal sealed class SystemOneCommands(ISystemOneProbe probe, IGuildConfigService configs, IMemberRoleClient roles)
{
    [Command("test"), Description("Send a sample decision and report whether the endpoint answers.")]
    public async Task Test(SlashCommandContext ctx)
    {
        await ctx.DeferResponseAsync(true);
        SystemOneProbeReport report = await probe.ProbeAsync();

        DiscordColor color = report.Status switch
        {
            SystemOneProbeStatus.Succeeded => new DiscordColor(0x00FF00),
            SystemOneProbeStatus.NotConfigured => new DiscordColor(0xFFAA00),
            _ => new DiscordColor(0xFF0000)
        };
        DiscordEmbedBuilder embed = new()
        {
            Title = "System One connection test",
            Description = report.Status switch
            {
                SystemOneProbeStatus.Succeeded => "The endpoint accepted a decision request and returned a verdict.",
                SystemOneProbeStatus.NotConfigured =>
                    "Bot:SystemOne:BaseUrl is empty, so no request was sent.",
                _ => "The endpoint did not return a decision. Membership review is unchanged."
            },
            Color = color
        };

        embed.AddField("Result", report.Status.ToString(), true);
        try
        {
            GuildConfig? config = await configs.GetFreshAsync(ctx.RequireGuild().Id);
            if (config?.Questionnaires.TryGetValue(QuestionnaireKeys.Member, out Questionnaire? questionnaire) == true)
            {
                embed.AddField("Questionnaire screening", questionnaire.Automation.Enabled ? "Enabled" : "Disabled", true);
                embed.AddField("Auto-promote at", SystemOneReportText.Percent(questionnaire.Automation.AutoPromoteNormalProbability), true);
                await AddReadinessChecksAsync(embed,
                    () => roles.CheckPromotionAsync(config, CancellationToken.None),
                    async () =>
                    {
                        if (questionnaire.SubmissionChannelId == 0)
                        {
                            return "Submission channel is not configured";
                        }

                        DiscordGuild guild = ctx.RequireGuild();
                        DiscordChannel channel;
                        try
                        {
                            channel = guild.FindChannel(questionnaire.SubmissionChannelId) ??
                                      await guild.GetChannelAsync(questionnaire.SubmissionChannelId);
                        }
                        catch (NotFoundException)
                        {
                            return "Configured submission channel does not exist";
                        }
                        catch (InvalidOperationException)
                        {
                            return "Configured submission channel belongs to a different server";
                        }

                        if (channel.GuildId != guild.Id)
                        {
                            return "Configured submission channel belongs to a different server";
                        }

                        DiscordMember bot = await ctx.RequireGuild().GetMemberAsync(ctx.Client.CurrentUser!.Id, true);
                        DiscordPermissions permissions = channel.PermissionsFor(bot);
                        return permissions.HasPermission(DiscordPermission.ViewChannel) &&
                               permissions.HasPermission(DiscordPermission.ReadMessageHistory) &&
                               permissions.HasPermission(DiscordPermission.SendMessages) &&
                               permissions.HasPermission(DiscordPermission.EmbedLinks)
                            ? "Ready" : "Needs View Channel, Read Message History, Send Messages and Embed Links";
                    }, ex => ctx.Client.Logger.LogWarning(ex,
                        "System One readiness check failed for guild {GuildId}", ctx.RequireGuild().Id));
            }
            else
            {
                embed.AddField("Questionnaire screening", "Member questionnaire not configured");
            }
        }
        catch (Exception ex)
        {
            embed.AddField("Configuration readiness", $"Could not verify roles or submission channel ({ex.GetType().Name}). Check configured IDs, permissions and the bot log.");
            ctx.Client.Logger.LogWarning(ex, "System One configuration readiness check failed for guild {GuildId}", ctx.RequireGuild().Id);
        }
        embed.AddField("Authorization", report.UsedAuthorization ? "Bearer token configured" : "None", true);
        if (!string.IsNullOrWhiteSpace(report.Endpoint))
        {
            embed.AddField("Endpoint", report.Endpoint, true);
        }

        if (!string.IsNullOrWhiteSpace(report.Model))
        {
            embed.AddField("Model", SystemOneReportText.Trim(report.Model), true);
        }

        if (report.Status != SystemOneProbeStatus.NotConfigured)
        {
            embed.AddField("Elapsed", $"{report.Elapsed.TotalMilliseconds:0} ms", true);
        }

        if (report.Status == SystemOneProbeStatus.Succeeded)
        {
            embed.AddField("Verdict", SystemOneReportText.Trim(report.Label ?? "n/a"), true);
            embed.AddField("Confidence", SystemOneReportText.Percent(report.Confidence), true);
            embed.AddField("Probabilities", SystemOneReportText.Trim(SystemOneReportText.FormatProbabilities(report.Probabilities)));
        }

        if (!string.IsNullOrWhiteSpace(report.Failure))
        {
            embed.AddField("Failure", SystemOneReportText.Trim(report.Failure));
        }

        await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(embed));
    }

    [Command("simulate"), Description("Classify a synthetic application with the current screening context.")]
    public async Task Simulate(
        SlashCommandContext ctx,
        [Parameter("application"), Description("Synthetic application text; no member or role is changed")]
        [MinMaxLength(1, SystemOneSimulation.MaximumApplicationLength)]
        string application)
    {
        await ctx.DeferResponseAsync(true);
        ulong guildId = ctx.RequireGuild().Id;
        GuildConfig? config;
        try
        {
            config = await configs.GetFreshAsync(guildId);
        }
        catch (Exception ex)
        {
            ctx.Client.Logger.LogWarning(ex,
                "System One simulation could not load configuration for guild {GuildId}", guildId);
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(
                SystemOneSimulation.Failure("The current server configuration could not be loaded. No membership data was changed.")));
            return;
        }

        if (config?.Questionnaires.TryGetValue(QuestionnaireKeys.Member, out Questionnaire? questionnaire) != true ||
            questionnaire is null)
        {
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "System One simulation",
                Description = "The Member questionnaire is not configured. No membership data was changed.",
                Color = new DiscordColor(0xFFAA00)
            }));
            return;
        }

        try
        {
            OnboardingSubmission submission = SystemOneSimulation.CreateSubmission(questionnaire, application);
            SystemOneProbeReport report = await probe.SimulateAsync(
                submission, questionnaire.Automation.AutoPromoteNormalProbability);
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(
                SystemOneSimulation.Format(report, questionnaire.Automation.Enabled)));
        }
        catch (ArgumentException ex)
        {
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(
                SystemOneSimulation.Failure($"{ex.Message} No membership data was changed.")));
        }
        catch (Exception ex)
        {
            ctx.Client.Logger.LogWarning(ex, "System One simulation failed for guild {GuildId}", guildId);
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(
                SystemOneSimulation.Failure(
                    $"The simulation could not be completed ({ex.GetType().Name}). No membership data was changed.")));
        }
    }

    internal static async Task AddReadinessChecksAsync(DiscordEmbedBuilder embed,
        Func<Task<string?>> promotionCheck, Func<Task<string>> submissionCheck, Action<Exception> logFailure)
    {
        try
        {
            embed.AddField("Promotion readiness", await promotionCheck() ?? "Ready");
        }
        catch (Exception ex)
        {
            logFailure(ex);
            embed.AddField("Promotion readiness", $"Could not verify ({ex.GetType().Name}). Check the bot log.");
        }

        try
        {
            embed.AddField("Submission channel access", await submissionCheck());
        }
        catch (Exception ex)
        {
            logFailure(ex);
            embed.AddField("Submission channel access", $"Could not verify ({ex.GetType().Name}). Check the bot log.");
        }
    }

}

internal static class SystemOneSimulation
{
    /// <summary>
    ///     Discord accepts at most 6000 characters for a slash-command string option.
    ///     System One also requires the shared state to be nonempty.
    /// </summary>
    public const int MaximumApplicationLength = 6000;

    public static OnboardingSubmission CreateSubmission(Questionnaire questionnaire, string application)
    {
        string text = application.Trim();
        if (text.Length == 0)
        {
            throw new ArgumentException("Provide the synthetic application.");
        }

        if (text.Length > MaximumApplicationLength)
        {
            throw new ArgumentException(
                $"Synthetic application must be {MaximumApplicationLength} characters or fewer.");
        }

        return new OnboardingSubmission
        {
            QuestionnaireName = questionnaire.Name,
            Description = questionnaire.Description,
            Context = questionnaire.Automation.Context,
            Answers = [new OnboardingAnswer("Synthetic application", text)]
        };
    }

    public static DiscordEmbed Failure(string description) =>
        new DiscordEmbedBuilder
        {
            Title = "System One simulation",
            Description = description,
            Color = new DiscordColor(0xFF0000)
        }.Build();

    public static DiscordEmbed Format(SystemOneProbeReport report, bool screeningEnabled)
    {
        DiscordEmbedBuilder embed = new()
        {
            Title = "System One simulation",
            Description = report.Status switch
            {
                SystemOneProbeStatus.Succeeded =>
                    "The endpoint classified this synthetic application. No member, application, database record, or role was changed.",
                SystemOneProbeStatus.NotConfigured =>
                    "Bot:SystemOne:BaseUrl is empty, so no request was sent. No membership data was changed.",
                _ => "The endpoint did not return a usable decision. No membership data was changed."
            },
            Color = report.Status switch
            {
                SystemOneProbeStatus.Succeeded => new DiscordColor(0x00FF00),
                SystemOneProbeStatus.NotConfigured => new DiscordColor(0xFFAA00),
                _ => new DiscordColor(0xFF0000)
            }
        };

        embed.AddField("Result", report.Status.ToString(), true);
        embed.AddField("Recommendation", SystemOneReportText.Trim(report.Label ?? "n/a"), true);
        embed.AddField("Action", Action(report, screeningEnabled), true);
        embed.AddField("Threshold", SystemOneReportText.Percent(report.Threshold), true);
        embed.AddField("Confidence", SystemOneReportText.Percent(report.Confidence), true);
        embed.AddField("Probabilities", SystemOneReportText.Trim(SystemOneReportText.FormatProbabilities(report.Probabilities)));
        if (!string.IsNullOrWhiteSpace(report.Model))
        {
            embed.AddField("Model", SystemOneReportText.Trim(report.Model), true);
        }

        if (!string.IsNullOrWhiteSpace(report.Failure))
        {
            embed.AddField("Failure", SystemOneReportText.Trim(report.Failure));
        }

        return embed.Build();
    }

    private static string Action(SystemOneProbeReport report, bool screeningEnabled)
    {
        string action = report.Outcome switch
        {
            OnboardingDecisionOutcome.AutoPromote => screeningEnabled
                ? "automatic promotion"
                : "automatic promotion if screening is enabled",
            OnboardingDecisionOutcome.ManualReview => "manual review",
            _ => "n/a"
        };

        return screeningEnabled ? action : $"screening is disabled; {action}";
    }

}

internal static class SystemOneReportText
{
    public static string FormatProbabilities(IReadOnlyDictionary<string, double> probabilities)
    {
        if (probabilities.Count == 0)
        {
            return "n/a";
        }

        return string.Join(", ", probabilities.Select(pair => $"{pair.Key} {Percent(pair.Value)}"));
    }

    public static string Percent(double? value) =>
        value is null ? "n/a" : value.Value.ToString("P0", CultureInfo.InvariantCulture);

    public static string Trim(string value) => value.Length <= 1024 ? value : value[..1021] + "...";
}
