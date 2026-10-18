using FluentAssertions;

using IgorBot.Schema;
using IgorBot.Services;

namespace IgorBot.Tests.Services;

public sealed class LurkerReconciliationPolicyTests
{
    private const ulong Stranger = 10;

    [Fact]
    public void HasOnlyStrangerRole_OnlyStranger_True() =>
        LurkerReconciliationPolicy.HasOnlyStrangerRole(false, [Stranger], Stranger).Should().BeTrue();

    [Fact]
    public void HasOnlyStrangerRole_Bot_False() =>
        LurkerReconciliationPolicy.HasOnlyStrangerRole(true, [Stranger], Stranger).Should().BeFalse();

    [Fact]
    public void HasOnlyStrangerRole_ExtraRole_False() =>
        LurkerReconciliationPolicy.HasOnlyStrangerRole(false, [Stranger, 11], Stranger).Should().BeFalse();

    [Fact]
    public void HasOnlyStrangerRole_WrongOrNoRole_False()
    {
        LurkerReconciliationPolicy.HasOnlyStrangerRole(false, [11], Stranger).Should().BeFalse();
        LurkerReconciliationPolicy.HasOnlyStrangerRole(false, [], Stranger).Should().BeFalse();
    }

    [Fact]
    public void NeedsOnboarding_FreshMember_True() =>
        LurkerReconciliationPolicy.NeedsOnboarding(Member()).Should().BeTrue();

    [Fact]
    public void NeedsOnboarding_InProgress_False() =>
        LurkerReconciliationPolicy.NeedsOnboarding(Member(true))
            .Should().BeFalse();

    [Fact]
    public void RotateAfter_StartsAfterCursorAndWraps() =>
        LurkerReconciliationPolicy.RotateAfter<ulong>([5, 1, 9, 3], x => x, 3)
            .Should().Equal(5UL, 9UL, 1UL, 3UL);

    [Fact]
    public void RotateAfter_NoCursorOrPastEnd_KeepsSortedOrder()
    {
        LurkerReconciliationPolicy.RotateAfter<ulong>([5, 1, 3], x => x, 0).Should().Equal(1UL, 3UL, 5UL);
        LurkerReconciliationPolicy.RotateAfter<ulong>([5, 1, 3], x => x, 9).Should().Equal(1UL, 3UL, 5UL);
    }

    private static GuildMember Member(bool inProgress = false) => new()
    {
        GuildId = 1, MemberId = 2, Member = "m", Mention = "<@2>", IsOnboardingInProgress = inProgress
    };
}
