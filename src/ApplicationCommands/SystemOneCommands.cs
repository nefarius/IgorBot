using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;

using IgorBot.Services;

namespace IgorBot.ApplicationCommands;

[Command("systemone"), Description("Check the System One decision endpoint. Administrator only.")]
[RequirePermissions(DiscordPermission.Administrator)]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal sealed class SystemOneCommands(ISystemOneProbe probe)
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
            embed.AddField("Verdict", report.Label ?? "n/a", true);
            embed.AddField("Confidence", Percent(report.Confidence), true);
            embed.AddField("Probabilities", Trim(FormatProbabilities(report.Probabilities)));
        }

        if (!string.IsNullOrWhiteSpace(report.Failure))
        {
            embed.AddField("Failure", Trim(report.Failure));
        }

        await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(embed));
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
