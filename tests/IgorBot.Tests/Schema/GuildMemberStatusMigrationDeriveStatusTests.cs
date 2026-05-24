using FluentAssertions;

using IgorBot.Schema;
using IgorBot.Services;

namespace IgorBot.Tests.Schema;

/// <summary>
///     Covers the full <see cref="GuildMemberStatusMigration.DeriveStatus" /> precedence chain
///     (highest to lowest):
///     BannedAt → KickedAt → AutoKickedAt → LeftAt → Promoted/FullMemberAt →
///     QuestionnaireSubmitted → Onboarding → StrangerRoleRemoved → New (fallback).
/// </summary>
public sealed class GuildMemberStatusMigrationDeriveStatusTests
{
    // ─── BannedAt (highest precedence) ───────────────────────────────────────

    [Fact]
    public void DeriveStatus_BannedAt_WithRemovedByModeration_ReturnsBannedByModerator()
    {
        GuildMember m = Base();
        DateTime ts = Ts(-5);
        m.BannedAt = ts;
        m.RemovedByModeration = true;

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.BannedByModerator);
        at.Should().Be(ts);
    }

    [Fact]
    public void DeriveStatus_BannedAt_WithoutRemovedByModeration_ReturnsBannedExternally()
    {
        GuildMember m = Base();
        DateTime ts = Ts(-5);
        m.BannedAt = ts;

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.BannedExternally);
        at.Should().Be(ts);
    }

    [Fact]
    public void DeriveStatus_BannedAt_TakesPrecedenceOverKickedAt()
    {
        GuildMember m = Base();
        m.BannedAt = Ts(-5);
        m.KickedAt = Ts(-4);

        (MemberStatus status, _) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.BannedExternally);
    }

    // ─── KickedAt ─────────────────────────────────────────────────────────────

    [Fact]
    public void DeriveStatus_KickedAt_WithRemovedByModeration_ReturnsKickedByModerator()
    {
        GuildMember m = Base();
        DateTime ts = Ts(-5);
        m.KickedAt = ts;
        m.RemovedByModeration = true;

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.KickedByModerator);
        at.Should().Be(ts);
    }

    [Fact]
    public void DeriveStatus_KickedAt_WithoutRemovedByModeration_ReturnsKickedExternally()
    {
        GuildMember m = Base();
        DateTime ts = Ts(-5);
        m.KickedAt = ts;

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.KickedExternally);
        at.Should().Be(ts);
    }

    [Fact]
    public void DeriveStatus_KickedAt_TakesPrecedenceOverAutoKickedAt()
    {
        GuildMember m = Base();
        m.KickedAt = Ts(-5);
        m.AutoKickedAt = Ts(-4);

        (MemberStatus status, _) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.KickedExternally);
    }

    // ─── AutoKickedAt ─────────────────────────────────────────────────────────

    [Fact]
    public void DeriveStatus_AutoKickedAt_ReturnsAutoKicked()
    {
        GuildMember m = Base();
        DateTime ts = Ts(-5);
        m.AutoKickedAt = ts;

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.AutoKicked);
        at.Should().Be(ts);
    }

    [Fact]
    public void DeriveStatus_AutoKickedAt_TakesPrecedenceOverLeftAt()
    {
        GuildMember m = Base();
        m.AutoKickedAt = Ts(-5);
        m.LeftAt = Ts(-4);

        (MemberStatus status, _) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.AutoKicked);
    }

    // ─── LeftAt ───────────────────────────────────────────────────────────────

    [Fact]
    public void DeriveStatus_LeftAt_ReturnsLeftVoluntarily()
    {
        GuildMember m = Base();
        DateTime ts = Ts(-5);
        m.LeftAt = ts;

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.LeftVoluntarily);
        at.Should().Be(ts);
    }

    // ─── PromotedAt / FullMemberAt ────────────────────────────────────────────

    [Fact]
    public void DeriveStatus_PromotedAt_ReturnsFullMember()
    {
        GuildMember m = Base();
        DateTime ts = Ts(-5);
        m.PromotedAt = ts;

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.FullMember);
        at.Should().Be(ts);
    }

    [Fact]
    public void DeriveStatus_FullMemberAt_ReturnsFullMember_WhenNoPromotedAt()
    {
        GuildMember m = Base();
        DateTime ts = Ts(-5);
        m.FullMemberAt = ts;

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.FullMember);
        at.Should().Be(ts);
    }

    [Fact]
    public void DeriveStatus_PromotedAt_PrefersPromotedAtOverFullMemberAt()
    {
        GuildMember m = Base();
        DateTime promotedTs = Ts(-10);
        DateTime fullMemberTs = Ts(-5);
        m.PromotedAt = promotedTs;
        m.FullMemberAt = fullMemberTs;

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.FullMember);
        at.Should().Be(promotedTs);
    }

    // ─── QuestionnaireSubmitted ───────────────────────────────────────────────

    [Fact]
    public void DeriveStatus_ApplicationWithQuestionnaireSubmittedAt_ReturnsQuestionnaireSubmitted()
    {
        GuildMember m = Base();
        DateTime ts = Ts(-5);
        SetApplication(m, new StrangerApplicationEmbed
        {
            GuildId = 1, ChannelId = 2, MessageId = 3, ID = "1-2-3",
            QuestionnaireSubmittedAt = ts
        });

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.QuestionnaireSubmitted);
        at.Should().Be(ts);
    }

    // ─── Onboarding ───────────────────────────────────────────────────────────

    [Fact]
    public void DeriveStatus_ApplicationWithoutQuestionnaire_ReturnsOnboarding_UsesApplicationCreatedAt()
    {
        GuildMember m = Base();
        DateTime ts = Ts(-5);
        SetApplication(m, new StrangerApplicationEmbed
        {
            GuildId = 1, ChannelId = 2, MessageId = 3, ID = "1-2-3",
            CreatedAt = ts
        });

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.Onboarding);
        at.Should().Be(ts);
    }

    [Fact]
    public void DeriveStatus_ChannelOnly_ReturnsOnboarding_UsesJoinedAt()
    {
        GuildMember m = Base();
        m.JoinedAt = Ts(-10);
        SetChannel(m, new NewbieChannel { GuildId = 1, ChannelId = 2, ChannelName = "ch", Mention = "<#2>", ID = "1-2" });

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.Onboarding);
        at.Should().Be(m.JoinedAt);
    }

    // ─── StrangerRoleRemoved ─────────────────────────────────────────────────

    [Fact]
    public void DeriveStatus_StrangerRoleRemovedAt_ReturnsStrangerRoleRemoved()
    {
        GuildMember m = Base();
        DateTime ts = Ts(-5);
        m.StrangerRoleRemovedAt = ts;

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.StrangerRoleRemoved);
        at.Should().Be(ts);
    }

    // ─── New (fallback) ───────────────────────────────────────────────────────

    [Fact]
    public void DeriveStatus_NoTimestamps_ReturnsNew_UsesJoinedAt()
    {
        GuildMember m = Base();
        m.JoinedAt = Ts(-10);

        (MemberStatus status, DateTime at) = GuildMemberStatusMigration.DeriveStatus(m);

        status.Should().Be(MemberStatus.New);
        at.Should().Be(m.JoinedAt);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static GuildMember Base() =>
        new()
        {
            GuildId = 1UL, MemberId = 2UL,
            Member = "u", Mention = "<@2>",
            ID = "1-2",
            Status = MemberStatus.Unknown
        };

    private static DateTime Ts(int offsetHours) => DateTime.UtcNow.AddHours(offsetHours);

    private static void SetApplication(GuildMember m, StrangerApplicationEmbed? app) =>
        SetPrivate(m, "Application", app);

    private static void SetChannel(GuildMember m, NewbieChannel? channel) =>
        SetPrivate(m, "Channel", channel);

    private static void SetPrivate(object obj, string name, object? value)
    {
        System.Reflection.PropertyInfo? prop = obj.GetType()
            .GetProperty(name,
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance);
        prop?.SetValue(obj, value);
    }
}
