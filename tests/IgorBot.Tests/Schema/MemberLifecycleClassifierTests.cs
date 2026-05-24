using FluentAssertions;

using IgorBot.Schema;

namespace IgorBot.Tests.Schema;

public sealed class MemberLifecycleClassifierTests
{
    // ─── Status-known path ───────────────────────────────────────────────────

    [Theory]
    [InlineData(MemberStatus.New)]
    [InlineData(MemberStatus.Onboarding)]
    [InlineData(MemberStatus.QuestionnaireSubmitted)]
    [InlineData(MemberStatus.FullMember)]
    [InlineData(MemberStatus.StrangerRoleRemoved)]
    public void IsEligibleForVoluntaryLeave_NonTerminalActiveStatuses_ReturnsTrue(MemberStatus status)
    {
        GuildMember member = BuildMember(status);

        MemberLifecycleClassifier.IsEligibleForVoluntaryLeave(member).Should().BeTrue();
    }

    [Theory]
    [InlineData(MemberStatus.LeftVoluntarily)]
    [InlineData(MemberStatus.KickedByModerator)]
    [InlineData(MemberStatus.BannedByModerator)]
    [InlineData(MemberStatus.AutoKicked)]
    [InlineData(MemberStatus.BannedByHoneypot)]
    [InlineData(MemberStatus.BannedExternally)]
    [InlineData(MemberStatus.KickedExternally)]
    public void IsEligibleForVoluntaryLeave_TerminalStatuses_ReturnsFalse(MemberStatus status)
    {
        GuildMember member = BuildMember(status);

        MemberLifecycleClassifier.IsEligibleForVoluntaryLeave(member).Should().BeFalse();
    }

    // ─── Legacy Unknown path ─────────────────────────────────────────────────

    [Fact]
    public void IsEligibleForVoluntaryLeave_Unknown_NoTerminalTimestamps_ReturnsTrue()
    {
        GuildMember member = BuildMember(MemberStatus.Unknown);

        MemberLifecycleClassifier.IsEligibleForVoluntaryLeave(member).Should().BeTrue();
    }

    [Fact]
    public void IsEligibleForVoluntaryLeave_Unknown_KickedAtSet_ReturnsFalse()
    {
        GuildMember member = BuildMember(MemberStatus.Unknown);
        member.KickedAt = DateTime.UtcNow;

        MemberLifecycleClassifier.IsEligibleForVoluntaryLeave(member).Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForVoluntaryLeave_Unknown_BannedAtSet_ReturnsFalse()
    {
        GuildMember member = BuildMember(MemberStatus.Unknown);
        member.BannedAt = DateTime.UtcNow;

        MemberLifecycleClassifier.IsEligibleForVoluntaryLeave(member).Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForVoluntaryLeave_Unknown_AutoKickedAtSet_ReturnsFalse()
    {
        GuildMember member = BuildMember(MemberStatus.Unknown);
        member.AutoKickedAt = DateTime.UtcNow;

        MemberLifecycleClassifier.IsEligibleForVoluntaryLeave(member).Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForVoluntaryLeave_Unknown_AllTerminalTimestampsSet_ReturnsFalse()
    {
        GuildMember member = BuildMember(MemberStatus.Unknown);
        member.KickedAt = DateTime.UtcNow;
        member.BannedAt = DateTime.UtcNow;
        member.AutoKickedAt = DateTime.UtcNow;

        MemberLifecycleClassifier.IsEligibleForVoluntaryLeave(member).Should().BeFalse();
    }

    // ─── ClassifyVoluntaryLeavePath ──────────────────────────────────────────

    [Fact]
    public void ClassifyVoluntaryLeavePath_Unknown_NoHistoryNoApplication_ReturnsLegacyUnknown()
    {
        GuildMember member = BuildMember(MemberStatus.Unknown);

        MemberLifecycleClassifier.ClassifyVoluntaryLeavePath(member)
            .Should().Be(VoluntaryLeavePath.LegacyUnknown);
    }

    [Fact]
    public void ClassifyVoluntaryLeavePath_New_SingleDiscoveredBySyncHistory_ReturnsLegacyDiscoveredBySync()
    {
        GuildMember member = BuildMember(MemberStatus.New);
        member.StatusHistory.Add(new MemberStatusEvent
        {
            From = MemberStatus.Unknown,
            To = MemberStatus.New,
            At = DateTime.UtcNow,
            Reason = MemberLifecycleClassifier.DiscoveredBySyncReason
        });

        MemberLifecycleClassifier.ClassifyVoluntaryLeavePath(member)
            .Should().Be(VoluntaryLeavePath.LegacyDiscoveredBySync);
    }

    [Fact]
    public void ClassifyVoluntaryLeavePath_New_SingleHistoryWithDifferentReason_ReturnsStandard()
    {
        GuildMember member = BuildMember(MemberStatus.New);
        member.StatusHistory.Add(new MemberStatusEvent
        {
            From = MemberStatus.Unknown,
            To = MemberStatus.New,
            At = DateTime.UtcNow,
            Reason = "rejoin"
        });

        MemberLifecycleClassifier.ClassifyVoluntaryLeavePath(member)
            .Should().Be(VoluntaryLeavePath.Standard);
    }

    [Theory]
    [InlineData(MemberStatus.Onboarding)]
    [InlineData(MemberStatus.QuestionnaireSubmitted)]
    [InlineData(MemberStatus.FullMember)]
    [InlineData(MemberStatus.StrangerRoleRemoved)]
    public void ClassifyVoluntaryLeavePath_OnboardedStatuses_ReturnsStandard(MemberStatus status)
    {
        GuildMember member = BuildMember(status);

        MemberLifecycleClassifier.ClassifyVoluntaryLeavePath(member)
            .Should().Be(VoluntaryLeavePath.Standard);
    }

    [Fact]
    public void ClassifyVoluntaryLeavePath_New_DiscoveredBySyncReason_ButMultipleHistoryEntries_ReturnsStandard()
    {
        GuildMember member = BuildMember(MemberStatus.New);
        member.StatusHistory.Add(new MemberStatusEvent
        {
            From = MemberStatus.Unknown,
            To = MemberStatus.New,
            At = DateTime.UtcNow.AddDays(-2),
            Reason = MemberLifecycleClassifier.DiscoveredBySyncReason
        });
        member.StatusHistory.Add(new MemberStatusEvent
        {
            From = MemberStatus.New,
            To = MemberStatus.New,
            At = DateTime.UtcNow,
            Reason = "rejoin"
        });

        MemberLifecycleClassifier.ClassifyVoluntaryLeavePath(member)
            .Should().Be(VoluntaryLeavePath.Standard);
    }

    // Regression guard: a member discovered by sync is still eligible for voluntary leave.
    [Fact]
    public void IsEligibleForVoluntaryLeave_New_DiscoveredBySyncHistory_ReturnsTrue()
    {
        GuildMember member = BuildMember(MemberStatus.New);
        member.StatusHistory.Add(new MemberStatusEvent
        {
            From = MemberStatus.Unknown,
            To = MemberStatus.New,
            At = DateTime.UtcNow,
            Reason = MemberLifecycleClassifier.DiscoveredBySyncReason
        });

        MemberLifecycleClassifier.IsEligibleForVoluntaryLeave(member).Should().BeTrue();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static GuildMember BuildMember(MemberStatus status) =>
        new()
        {
            GuildId = 1UL,
            MemberId = 2UL,
            Member = "testuser",
            Mention = "<@2>",
            ID = "1-2",
            Status = status
        };
}
