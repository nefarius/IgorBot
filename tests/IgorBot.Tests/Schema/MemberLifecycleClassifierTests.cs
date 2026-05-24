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
