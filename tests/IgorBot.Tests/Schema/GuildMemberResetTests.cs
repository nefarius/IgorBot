using FluentAssertions;

using IgorBot.Schema;

namespace IgorBot.Tests.Schema;

public sealed class GuildMemberResetTests
{
    [Fact]
    public void Reset_ClearsAllLegacyTimestampsAndFlags()
    {
        GuildMember member = BuildPopulated();

        member.Reset();

        member.LeftAt.Should().BeNull();
        member.KickedAt.Should().BeNull();
        member.BannedAt.Should().BeNull();
        member.AutoKickedAt.Should().BeNull();
        member.PromotedAt.Should().BeNull();
        member.FullMemberAt.Should().BeNull();
        member.StrangerRoleRemovedAt.Should().BeNull();
        member.RemovedByModeration.Should().BeFalse();
    }

    [Fact]
    public void Reset_SetsStatusToNew()
    {
        GuildMember member = BuildPopulated();

        member.Reset();

        member.Status.Should().Be(MemberStatus.New);
    }

    [Fact]
    public void Reset_SetsStatusChangedAtAndReasonToRejoin()
    {
        DateTime before = DateTime.UtcNow;
        GuildMember member = BuildPopulated();

        member.Reset();

        member.StatusChangedAt.Should().NotBeNull();
        member.StatusChangedAt!.Value.Should().BeOnOrAfter(before);
        member.StatusReason.Should().Be("rejoin");
    }

    [Fact]
    public void Reset_AppendsOneHistoryEventWithCorrectFromStatus()
    {
        GuildMember member = BuildPopulated(MemberStatus.Onboarding);
        int countBefore = member.StatusHistory.Count;

        member.Reset();

        member.StatusHistory.Should().HaveCount(countBefore + 1);
        MemberStatusEvent evt = member.StatusHistory.Last();
        evt.From.Should().Be(MemberStatus.Onboarding);
        evt.To.Should().Be(MemberStatus.New);
        evt.Reason.Should().Be("rejoin");
    }

    [Fact]
    public void Reset_NullsApplicationAndChannel()
    {
        GuildMember member = BuildPopulated();
        // Manually set via reflection because the setters are private
        SetPrivate(member, "Application", new StrangerApplicationEmbed
        {
            GuildId = 1,
            ChannelId = 2,
            MessageId = 3,
            ID = "1-2-3"
        });
        SetPrivate(member, "Channel", new NewbieChannel
        {
            GuildId = 1,
            ChannelId = 2,
            ChannelName = "newbie",
            Mention = "<#2>",
            ID = "ch1"
        });

        member.Reset();

        member.Application.Should().BeNull();
        member.Channel.Should().BeNull();
    }

    [Fact]
    public void Reset_PreservesExistingHistoryEntries()
    {
        GuildMember member = BuildPopulated();
        member.StatusHistory.Add(new MemberStatusEvent { From = MemberStatus.Unknown, To = MemberStatus.New, At = DateTime.UtcNow.AddDays(-1) });
        int countBefore = member.StatusHistory.Count;

        member.Reset();

        member.StatusHistory.Should().HaveCount(countBefore + 1);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static GuildMember BuildPopulated(MemberStatus status = MemberStatus.Onboarding)
    {
        GuildMember member = new()
        {
            GuildId = 1UL,
            MemberId = 2UL,
            Member = "testuser",
            Mention = "<@2>",
            ID = "1-2",
            Status = status,
            JoinedAt = DateTime.UtcNow.AddDays(-1)
        };

        member.LeftAt = DateTime.UtcNow.AddHours(-1);
        member.KickedAt = DateTime.UtcNow.AddHours(-2);
        member.BannedAt = DateTime.UtcNow.AddHours(-3);
        member.AutoKickedAt = DateTime.UtcNow.AddHours(-4);
        member.PromotedAt = DateTime.UtcNow.AddHours(-5);
        member.FullMemberAt = DateTime.UtcNow.AddHours(-6);
        member.StrangerRoleRemovedAt = DateTime.UtcNow.AddHours(-7);
        member.RemovedByModeration = true;

        return member;
    }

    private static void SetPrivate(object obj, string propertyName, object? value)
    {
        System.Reflection.PropertyInfo? prop = obj.GetType()
            .GetProperty(propertyName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        prop?.SetValue(obj, value);
    }
}
