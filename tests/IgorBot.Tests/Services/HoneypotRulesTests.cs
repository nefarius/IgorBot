using System.Runtime.CompilerServices;

using DSharpPlus.Exceptions;

using FluentAssertions;

using IgorBot.Schema;
using IgorBot.Services;

namespace IgorBot.Tests.Services;

public sealed class HoneypotRulesTests
{
    [Fact]
    public void Evaluate_Bot_ReturnsSkipBot()
    {
        HoneypotRules.Evaluate(true, false, [], []).Should().Be(HoneypotEligibility.SkipBot);
    }

    [Fact]
    public void Evaluate_Owner_ReturnsSkipOwner()
    {
        HoneypotRules.Evaluate(false, true, [], []).Should().Be(HoneypotEligibility.SkipOwner);
    }

    [Fact]
    public void Evaluate_ExcludedRole_ReturnsSkipExcludedRole()
    {
        HoneypotRules.Evaluate(false, false, [10UL, 20UL], [20UL])
            .Should().Be(HoneypotEligibility.SkipExcludedRole);
    }

    [Fact]
    public void Evaluate_NoExclusionMatch_ReturnsEnforce()
    {
        HoneypotRules.Evaluate(false, false, [10UL], [20UL])
            .Should().Be(HoneypotEligibility.Enforce);
    }

    [Fact]
    public void Evaluate_HumanWithoutRoles_ReturnsEnforce()
    {
        HoneypotRules.Evaluate(false, false, [], []).Should().Be(HoneypotEligibility.Enforce);
    }

    [Fact]
    public void Evaluate_BotTakesPrecedenceOverOwner()
    {
        HoneypotRules.Evaluate(true, true, [20UL], [20UL]).Should().Be(HoneypotEligibility.SkipBot);
    }

    [Theory]
    [InlineData(MemberStatus.BannedByHoneypot)]
    [InlineData(MemberStatus.BannedByModerator)]
    [InlineData(MemberStatus.BannedExternally)]
    public void IsTerminalBan_BanStatuses_ReturnsTrue(MemberStatus status)
    {
        HoneypotRules.IsTerminalBan(status).Should().BeTrue();
    }

    [Theory]
    [InlineData(MemberStatus.New)]
    [InlineData(MemberStatus.Onboarding)]
    [InlineData(MemberStatus.FullMember)]
    [InlineData(MemberStatus.AutoKicked)]
    [InlineData(MemberStatus.LeftVoluntarily)]
    public void IsTerminalBan_NonBanStatuses_ReturnsFalse(MemberStatus status)
    {
        HoneypotRules.IsTerminalBan(status).Should().BeFalse();
    }

    [Theory]
    [InlineData(MemberStatus.BannedByModerator)]
    [InlineData(MemberStatus.BannedExternally)]
    public void ShouldSkipDiscordBan_OtherBanPaths_ReturnsTrue(MemberStatus status)
    {
        HoneypotRules.ShouldSkipDiscordBan(status).Should().BeTrue();
    }

    [Fact]
    public void ShouldSkipDiscordBan_Honeypot_ReturnsFalse_SoRetryCanIssueBan()
    {
        HoneypotRules.ShouldSkipDiscordBan(MemberStatus.BannedByHoneypot).Should().BeFalse();
    }

    [Fact]
    public void IsPermanentBanFailure_Unauthorized_ReturnsTrue()
    {
        HoneypotRules.IsPermanentBanFailure(Uninitialized<UnauthorizedException>()).Should().BeTrue();
    }

    [Fact]
    public void IsPermanentBanFailure_NotFound_ReturnsTrue()
    {
        HoneypotRules.IsPermanentBanFailure(Uninitialized<NotFoundException>()).Should().BeTrue();
    }

    [Fact]
    public void IsPermanentBanFailure_Transient_ReturnsFalse()
    {
        HoneypotRules.IsPermanentBanFailure(new InvalidOperationException("discord 503")).Should().BeFalse();
    }

    private static Exception Uninitialized<T>() where T : Exception =>
        (Exception)RuntimeHelpers.GetUninitializedObject(typeof(T));
}
