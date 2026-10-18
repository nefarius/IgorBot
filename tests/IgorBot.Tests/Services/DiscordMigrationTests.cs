using DSharpPlus.Entities;

using FluentAssertions;

using IgorBot.Handlers;
using IgorBot.Schema;
using IgorBot.Services;
using IgorBot.Util;

using Microsoft.Extensions.Logging.Abstractions;

namespace IgorBot.Tests.Services;

public sealed class DiscordReadinessServiceTests
{
    [Fact]
    public void IsGuildReady_BeforeAnyEvent_IsFalse()
    {
        DiscordReadinessService readiness = new();
        readiness.IsGuildReady(1).Should().BeFalse();
    }

    [Fact]
    public void MarkGuildAvailable_Repeated_StaysReady()
    {
        DiscordReadinessService readiness = new();
        readiness.MarkGuildAvailable(7);
        readiness.MarkGuildAvailable(7);
        readiness.IsGuildReady(7).Should().BeTrue();
        readiness.IsGuildReady(8).Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsReadyGuilds()
    {
        DiscordReadinessService readiness = new();
        readiness.MarkGuildAvailable(7);
        readiness.Reset();
        readiness.IsGuildReady(7).Should().BeFalse();
    }
}

public sealed class DiscordGatewayHostedServiceTests
{
    [Fact]
    public async Task StartAsync_ResetsReadinessThenConnects()
    {
        RecordingGateway gateway = new();
        DiscordReadinessService readiness = new();
        readiness.MarkGuildAvailable(4);
        DiscordGatewayHostedService service = new(gateway, readiness, NullLogger<DiscordGatewayHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);

        gateway.Calls.Should().Equal("connect");
        readiness.IsGuildReady(4).Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_DisconnectsThenResets()
    {
        RecordingGateway gateway = new();
        DiscordReadinessService readiness = new();
        DiscordGatewayHostedService service = new(gateway, readiness, NullLogger<DiscordGatewayHostedService>.Instance);
        await service.StartAsync(CancellationToken.None);
        readiness.MarkGuildAvailable(4);

        await service.StopAsync(CancellationToken.None);

        gateway.Calls.Should().Equal("connect", "disconnect");
        readiness.IsGuildReady(4).Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_Cancelled_DoesNotConnect()
    {
        RecordingGateway gateway = new();
        DiscordReadinessService readiness = new();
        DiscordGatewayHostedService service = new(gateway, readiness, NullLogger<DiscordGatewayHostedService>.Instance);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Func<Task> act = () => service.StartAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        gateway.Calls.Should().BeEmpty();
    }

    private sealed class RecordingGateway : IDiscordGateway
    {
        public List<string> Calls { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add("connect");
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add("disconnect");
            return Task.CompletedTask;
        }
    }
}

public sealed class DiscordEntityIdTests
{
    [Fact]
    public void ForMember_JoinsGuildAndMember()
    {
        DiscordEntityIds.ForMember(10, 20).Should().Be("10-20");
    }
}

public sealed class MemberDeparturePolicyTests
{
    [Theory]
    [InlineData(VoluntaryLeavePath.Standard, "voluntary_leave")]
    [InlineData(VoluntaryLeavePath.LegacyUnknown, "legacy_unknown_voluntary_leave")]
    [InlineData(VoluntaryLeavePath.LegacyDiscoveredBySync, "legacy_discovered_voluntary_leave")]
    public void ReasonFor_MatchesClassifierPath(VoluntaryLeavePath path, string reason)
    {
        MemberDeparturePolicy.ReasonFor(path).Should().Be(reason);
    }

    [Fact]
    public void Grace_IsHalfASecond()
    {
        MemberDeparturePolicy.ConcurrentTerminalStatusGrace.Should().Be(TimeSpan.FromMilliseconds(500));
    }
}

public sealed class ModerationWidgetSpecTests
{
    [Fact]
    public void MemberActions_AreKickThenBan()
    {
        IReadOnlyList<ModerationButton> buttons = ModerationWidgetSpec.MemberActions("member-1");
        buttons.Select(button => (button.Style, button.CustomId, button.Label, button.Enabled)).Should().Equal(
            (ModerationButtonStyle.Secondary, "strangers|member-1|kick", "Kick", true),
            (ModerationButtonStyle.Danger, "strangers|member-1|ban", "Ban", true));
    }

    [Fact]
    public void SubmittedApplication_KeepsApproveAndRejectButtonsWithoutAClassifier()
    {
        IEnumerable<string> labels = ModerationWidgetSpec.MemberActions("member")
            .Concat(ModerationWidgetSpec.ApplicationActions("app", questionnaireSubmitted: true, autoKickEnabled: false))
            .Select(button => button.Label);

        labels.Should().Equal("Kick", "Ban", "Promote");
    }

    [Fact]
    public void ApplicationActions_DependOnSubmissionAndAutoKick()
    {
        ModerationWidgetSpec.ApplicationActions("app", questionnaireSubmitted: false, autoKickEnabled: true)
            .Select(button => button.Label).Should().Equal("Disable auto-kick");
        ModerationWidgetSpec.ApplicationActions("app", questionnaireSubmitted: true, autoKickEnabled: false)
            .Select(button => button.CustomId).Should().Equal("strangers|app|promote");
        ModerationWidgetSpec.ApplicationActions("app", false, false).Should().BeEmpty();
    }

    [Fact]
    public void DisableAll_AndEnableAll_ToggleEveryButton()
    {
        IReadOnlyList<ModerationButton> busy = ModerationWidgetSpec.DisableAll(ModerationWidgetSpec.MemberActions("id"));
        busy.Should().OnlyContain(button => !button.Enabled);
        ModerationWidgetSpec.EnableAll(busy).Should().OnlyContain(button => button.Enabled);
    }
}

public sealed class NewbieChannelAccessPolicyTests
{
    [Fact]
    public void Everyone_IsDeniedViewChannelOnly()
    {
        NewbieChannelAccessPolicy.EveryoneDenied.Should().Equal(DiscordPermission.ViewChannel);
    }

    [Fact]
    public void Moderator_CanModerateTheChannel()
    {
        NewbieChannelAccessPolicy.ModeratorAllowed.Should().Equal(
            DiscordPermission.ViewChannel,
            DiscordPermission.ReadMessageHistory,
            DiscordPermission.ManageMessages,
            DiscordPermission.SendMessages,
            DiscordPermission.EmbedLinks,
            DiscordPermission.AddReactions);
    }

    [Fact]
    public void Member_CanTalkButNotModerate()
    {
        NewbieChannelAccessPolicy.MemberAllowed.Should().Contain(DiscordPermission.AttachFiles);
        NewbieChannelAccessPolicy.MemberAllowed.Should().NotContain(DiscordPermission.ManageMessages);
        NewbieChannelAccessPolicy.ToPermissions(NewbieChannelAccessPolicy.MemberAllowed)
            .HasPermission(DiscordPermission.SendMessages).Should().BeTrue();
    }
}
