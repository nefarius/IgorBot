using DSharpPlus.Entities;

using FluentAssertions;

using IgorBot.ApplicationCommands;

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
}
