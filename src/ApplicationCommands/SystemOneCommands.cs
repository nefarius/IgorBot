using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;

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
                embed.AddField("Auto-promote at", Percent(questionnaire.Automation.AutoPromoteNormalProbability), true);
                await AddReadinessChecksAsync(embed,
                    () => roles.CheckPromotionAsync(config, CancellationToken.None),
                    async () =>
                    {
                        DiscordChannel channel = await ctx.RequireGuild().GetChannelAsync(questionnaire.SubmissionChannelId);
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
            embed.AddField("Model", Trim(report.Model), true);
        }

        if (report.Status != SystemOneProbeStatus.NotConfigured)
        {
            embed.AddField("Elapsed", $"{report.Elapsed.TotalMilliseconds:0} ms", true);
        }

        if (report.Status == SystemOneProbeStatus.Succeeded)
        {
            embed.AddField("Verdict", Trim(report.Label ?? "n/a"), true);
            embed.AddField("Confidence", Percent(report.Confidence), true);
            embed.AddField("Probabilities", Trim(FormatProbabilities(report.Probabilities)));
        }

        if (!string.IsNullOrWhiteSpace(report.Failure))
        {
            embed.AddField("Failure", Trim(report.Failure));
        }

        await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(embed));
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

    private static string FormatProbabilities(IReadOnlyDictionary<string, double> probabilities)
    {
        if (probabilities.Count == 0)
        {
            return "n/a";
        }

        return string.Join(", ", probabilities.Select(pair => $"{pair.Key} {Percent(pair.Value)}"));
    }

    private static string Percent(double? value) =>
        value is null ? "n/a" : value.Value.ToString("P0", CultureInfo.InvariantCulture);

    private static string Trim(string value) => value.Length <= 1024 ? value : value[..1021] + "...";
}
