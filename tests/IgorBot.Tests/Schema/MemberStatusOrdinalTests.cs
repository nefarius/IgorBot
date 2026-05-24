using FluentAssertions;

using IgorBot.Schema;

namespace IgorBot.Tests.Schema;

/// <summary>
///     Regression guard: ordinals are persisted as integers in MongoDB.
///     Any change to these values would silently corrupt stored data.
/// </summary>
public sealed class MemberStatusOrdinalTests
{
    [Theory]
    [InlineData(MemberStatus.Unknown, 0)]
    [InlineData(MemberStatus.New, 1)]
    [InlineData(MemberStatus.Onboarding, 2)]
    [InlineData(MemberStatus.QuestionnaireSubmitted, 3)]
    [InlineData(MemberStatus.FullMember, 4)]
    [InlineData(MemberStatus.LeftVoluntarily, 5)]
    [InlineData(MemberStatus.KickedByModerator, 6)]
    [InlineData(MemberStatus.BannedByModerator, 7)]
    [InlineData(MemberStatus.AutoKicked, 8)]
    [InlineData(MemberStatus.BannedByHoneypot, 9)]
    [InlineData(MemberStatus.BannedExternally, 10)]
    [InlineData(MemberStatus.KickedExternally, 11)]
    [InlineData(MemberStatus.StrangerRoleRemoved, 12)]
    public void MemberStatus_Ordinals_MustNotChange(MemberStatus status, int expectedOrdinal)
    {
        ((int)status).Should().Be(expectedOrdinal,
            because: $"{status} ordinals are persisted in MongoDB as integers and must never be reordered");
    }

    [Fact]
    public void MemberStatus_AllValuesHaveOrdinalCoverage()
    {
        // Fail if a new enum value is added without a corresponding ordinal test above.
        int definedValues = Enum.GetValues<MemberStatus>().Length;
        definedValues.Should().Be(13,
            because: "every new MemberStatus value must have its ordinal pinned in MemberStatusOrdinalTests");
    }
}
