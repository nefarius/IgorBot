using FluentAssertions;

using IgorBot.Schema;

namespace IgorBot.Tests.Schema;

public sealed class GuildMemberDerivedFlagsTests
{
    // ─── IsNew (Status-known path) ───────────────────────────────────────────

    [Theory]
    [InlineData(MemberStatus.New)]
    [InlineData(MemberStatus.Onboarding)]
    [InlineData(MemberStatus.QuestionnaireSubmitted)]
    public void IsNew_ActiveOnboardingStatuses_ReturnsTrue(MemberStatus status)
    {
        GuildMember member = Build(status);
        member.IsNew.Should().BeTrue();
    }

    [Theory]
    [InlineData(MemberStatus.FullMember)]
    [InlineData(MemberStatus.LeftVoluntarily)]
    [InlineData(MemberStatus.KickedByModerator)]
    [InlineData(MemberStatus.BannedByModerator)]
    [InlineData(MemberStatus.AutoKicked)]
    [InlineData(MemberStatus.BannedByHoneypot)]
    [InlineData(MemberStatus.BannedExternally)]
    [InlineData(MemberStatus.KickedExternally)]
    [InlineData(MemberStatus.StrangerRoleRemoved)]
    public void IsNew_NonActiveStatuses_ReturnsFalse(MemberStatus status)
    {
        GuildMember member = Build(status);
        member.IsNew.Should().BeFalse();
    }

    // ─── IsNew (Unknown / legacy path) ──────────────────────────────────────

    [Fact]
    public void IsNew_Unknown_NoTerminalTimestamps_ReturnsTrue()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.IsNew.Should().BeTrue();
    }

    [Fact]
    public void IsNew_Unknown_PromotedAt_ReturnsFalse()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.PromotedAt = DateTime.UtcNow;
        member.IsNew.Should().BeFalse();
    }

    [Fact]
    public void IsNew_Unknown_KickedAt_ReturnsFalse()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.KickedAt = DateTime.UtcNow;
        member.IsNew.Should().BeFalse();
    }

    [Fact]
    public void IsNew_Unknown_AutoKickedAt_ReturnsFalse()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.AutoKickedAt = DateTime.UtcNow;
        member.IsNew.Should().BeFalse();
    }

    [Fact]
    public void IsNew_Unknown_BannedAt_ReturnsFalse()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.BannedAt = DateTime.UtcNow;
        member.IsNew.Should().BeFalse();
    }

    [Fact]
    public void IsNew_Unknown_LeftAt_ReturnsFalse()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.LeftAt = DateTime.UtcNow;
        member.IsNew.Should().BeFalse();
    }

    // ─── HasLeftGuild (Status-known path) ────────────────────────────────────

    [Theory]
    [InlineData(MemberStatus.LeftVoluntarily)]
    [InlineData(MemberStatus.KickedByModerator)]
    [InlineData(MemberStatus.BannedByModerator)]
    [InlineData(MemberStatus.AutoKicked)]
    [InlineData(MemberStatus.BannedByHoneypot)]
    [InlineData(MemberStatus.BannedExternally)]
    [InlineData(MemberStatus.KickedExternally)]
    public void HasLeftGuild_DepartedStatuses_ReturnsTrue(MemberStatus status)
    {
        GuildMember member = Build(status);
        member.HasLeftGuild.Should().BeTrue();
    }

    [Theory]
    [InlineData(MemberStatus.New)]
    [InlineData(MemberStatus.Onboarding)]
    [InlineData(MemberStatus.QuestionnaireSubmitted)]
    [InlineData(MemberStatus.FullMember)]
    [InlineData(MemberStatus.StrangerRoleRemoved)]
    public void HasLeftGuild_PresentStatuses_ReturnsFalse(MemberStatus status)
    {
        GuildMember member = Build(status);
        member.HasLeftGuild.Should().BeFalse();
    }

    // ─── HasLeftGuild (Unknown / legacy path) ────────────────────────────────

    [Fact]
    public void HasLeftGuild_Unknown_NoTimestamps_ReturnsFalse()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.HasLeftGuild.Should().BeFalse();
    }

    [Fact]
    public void HasLeftGuild_Unknown_KickedAtAfterJoin_ReturnsTrue()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.JoinedAt = DateTime.UtcNow.AddHours(-1);
        member.KickedAt = DateTime.UtcNow;
        member.HasLeftGuild.Should().BeTrue();
    }

    [Fact]
    public void HasLeftGuild_Unknown_KickedAtBeforeJoin_ReturnsFalse()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.JoinedAt = DateTime.UtcNow;
        member.KickedAt = DateTime.UtcNow.AddHours(-1);
        member.HasLeftGuild.Should().BeFalse();
    }

    [Fact]
    public void HasLeftGuild_Unknown_BannedAtAfterJoin_ReturnsTrue()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.JoinedAt = DateTime.UtcNow.AddHours(-1);
        member.BannedAt = DateTime.UtcNow;
        member.HasLeftGuild.Should().BeTrue();
    }

    [Fact]
    public void HasLeftGuild_Unknown_AutoKickedAtAfterJoin_ReturnsTrue()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.JoinedAt = DateTime.UtcNow.AddHours(-1);
        member.AutoKickedAt = DateTime.UtcNow;
        member.HasLeftGuild.Should().BeTrue();
    }

    [Fact]
    public void HasLeftGuild_Unknown_LeftAtAfterJoin_ReturnsTrue()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.JoinedAt = DateTime.UtcNow.AddHours(-1);
        member.LeftAt = DateTime.UtcNow;
        member.HasLeftGuild.Should().BeTrue();
    }

    // ─── IsInGuild ────────────────────────────────────────────────────────────

    [Fact]
    public void IsInGuild_IsInverseOfHasLeftGuild()
    {
        foreach (MemberStatus status in Enum.GetValues<MemberStatus>())
        {
            GuildMember member = Build(status);
            member.IsInGuild.Should().Be(!member.HasLeftGuild,
                because: $"IsInGuild must be !HasLeftGuild for status {status}");
        }
    }

    // ─── IsFullMember (Status-known path) ────────────────────────────────────

    [Fact]
    public void IsFullMember_FullMemberStatus_ReturnsTrue()
    {
        GuildMember member = Build(MemberStatus.FullMember);
        member.IsFullMember.Should().BeTrue();
    }

    [Theory]
    [InlineData(MemberStatus.New)]
    [InlineData(MemberStatus.Onboarding)]
    [InlineData(MemberStatus.QuestionnaireSubmitted)]
    [InlineData(MemberStatus.LeftVoluntarily)]
    [InlineData(MemberStatus.KickedByModerator)]
    public void IsFullMember_NonFullMemberStatuses_ReturnsFalse(MemberStatus status)
    {
        GuildMember member = Build(status);
        member.IsFullMember.Should().BeFalse();
    }

    // ─── IsFullMember (Unknown / legacy path) ────────────────────────────────

    [Fact]
    public void IsFullMember_Unknown_PromotedAtSet_InGuild_ReturnsTrue()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        // HasLeftGuild = false (no exit timestamps)
        member.PromotedAt = DateTime.UtcNow;
        member.IsFullMember.Should().BeTrue();
    }

    [Fact]
    public void IsFullMember_Unknown_PromotedAtSet_HasLeft_ReturnsFalse()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.JoinedAt = DateTime.UtcNow.AddHours(-2);
        member.PromotedAt = DateTime.UtcNow.AddHours(-1);
        member.LeftAt = DateTime.UtcNow;
        member.IsFullMember.Should().BeFalse();
    }

    // ─── IsBanned (Status-known path) ────────────────────────────────────────

    [Theory]
    [InlineData(MemberStatus.BannedByModerator)]
    [InlineData(MemberStatus.BannedByHoneypot)]
    [InlineData(MemberStatus.BannedExternally)]
    public void IsBanned_BanStatuses_ReturnsTrue(MemberStatus status)
    {
        GuildMember member = Build(status);
        member.IsBanned.Should().BeTrue();
    }

    [Theory]
    [InlineData(MemberStatus.New)]
    [InlineData(MemberStatus.Onboarding)]
    [InlineData(MemberStatus.KickedByModerator)]
    [InlineData(MemberStatus.LeftVoluntarily)]
    [InlineData(MemberStatus.AutoKicked)]
    public void IsBanned_NonBanStatuses_ReturnsFalse(MemberStatus status)
    {
        GuildMember member = Build(status);
        member.IsBanned.Should().BeFalse();
    }

    // ─── IsBanned (Unknown / legacy path) ────────────────────────────────────

    [Fact]
    public void IsBanned_Unknown_BannedAtSet_ReturnsTrue()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.BannedAt = DateTime.UtcNow;
        member.IsBanned.Should().BeTrue();
    }

    [Fact]
    public void IsBanned_Unknown_NoBannedAt_ReturnsFalse()
    {
        GuildMember member = Build(MemberStatus.Unknown);
        member.IsBanned.Should().BeFalse();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static GuildMember Build(MemberStatus status) =>
        new()
        {
            GuildId = 10UL,
            MemberId = 20UL,
            Member = "user",
            Mention = "<@20>",
            ID = "10-20",
            Status = status
        };
}
