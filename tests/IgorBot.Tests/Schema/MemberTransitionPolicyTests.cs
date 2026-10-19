using FluentAssertions;

using IgorBot.Schema;

namespace IgorBot.Tests.Schema;

public sealed class MemberTransitionPolicyTests
{
    [Theory]
    [InlineData(MemberStatus.FullMember)]
    [InlineData(MemberStatus.LeftVoluntarily)]
    [InlineData(MemberStatus.KickedByModerator)]
    [InlineData(MemberStatus.BannedByModerator)]
    [InlineData(MemberStatus.AutoKicked)]
    [InlineData(MemberStatus.BannedByHoneypot)]
    public void StrangerRoleRemoved_IsNeverAllowedFrom(MemberStatus status)
    {
        MemberTransitionPolicy.AllowedFrom(MemberStatus.StrangerRoleRemoved)!.Should().NotContain(status);
    }

    [Fact]
    public void QuestionnaireSubmitted_OnlyFromOnboardingOrLegacyUnknown()
    {
        MemberTransitionPolicy.AllowedFrom(MemberStatus.QuestionnaireSubmitted)!
            .Should().BeEquivalentTo([MemberStatus.Unknown, MemberStatus.Onboarding]);
    }

    [Theory]
    [InlineData(MemberStatus.LeftVoluntarily)]
    [InlineData(MemberStatus.KickedByModerator)]
    [InlineData(MemberStatus.BannedByModerator)]
    [InlineData(MemberStatus.AutoKicked)]
    [InlineData(MemberStatus.KickedExternally)]
    [InlineData(MemberStatus.BannedExternally)]
    [InlineData(MemberStatus.BannedByHoneypot)]
    public void FullMember_IsNeverAllowedFromDeparted(MemberStatus status)
    {
        MemberTransitionPolicy.AllowedFrom(MemberStatus.FullMember)!.Should().NotContain(status);
    }

    [Fact]
    public void Onboarding_IsNotAllowedFromTerminalOrAdvancedStates()
    {
        IReadOnlyCollection<MemberStatus> allowed = MemberTransitionPolicy.AllowedFrom(MemberStatus.Onboarding)!;
        allowed.Should().NotContain(
        [
            MemberStatus.FullMember, MemberStatus.LeftVoluntarily, MemberStatus.QuestionnaireSubmitted,
            MemberStatus.BannedByHoneypot
        ]);
    }

    [Fact]
    public void AutoKicked_OnlyFromIdleCandidateStates()
    {
        MemberTransitionPolicy.AllowedFrom(MemberStatus.AutoKicked)!
            .Should().BeEquivalentTo([MemberStatus.Unknown, MemberStatus.Onboarding]);
    }

    [Fact]
    public void HoneypotBan_IsUnrestricted()
    {
        MemberTransitionPolicy.AllowedFrom(MemberStatus.BannedByHoneypot).Should().BeNull();
    }

    [Fact]
    public void ExternalKickUpgrade_OnlyForRecentVoluntaryLeave()
    {
        DateTime now = DateTime.UtcNow;
        MemberTransitionPolicy.CanUpgradeLeaveToExternalKick(
            MemberStatus.LeftVoluntarily, now.AddSeconds(-5), now).Should().BeTrue();
        MemberTransitionPolicy.CanUpgradeLeaveToExternalKick(
            MemberStatus.LeftVoluntarily, now.AddMinutes(-5), now).Should().BeFalse();
        MemberTransitionPolicy.CanUpgradeLeaveToExternalKick(
            MemberStatus.FullMember, now.AddSeconds(-5), now).Should().BeFalse();
        MemberTransitionPolicy.CanUpgradeLeaveToExternalKick(
            MemberStatus.LeftVoluntarily, null, now).Should().BeFalse();
    }
}
