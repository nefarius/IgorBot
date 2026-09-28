using FluentAssertions;

using IgorBot.Services;

namespace IgorBot.Tests.Services;

public sealed class HoneypotReconciliationEngineTests
{
    [Fact]
    public void ResolveAfterId_MissingCheckpoint_Uses24HourLookbackSnowflake()
    {
        DateTime now = new(2026, 9, 28, 16, 0, 0, DateTimeKind.Utc);

        ulong afterId = HoneypotReconciliationEngine.ResolveAfterId(null, now);

        ulong expected = HoneypotReconciliationEngine.SnowflakeFromTimestamp(
            now - HoneypotReconciliationEngine.InitialLookback);
        afterId.Should().Be(expected);
        afterId.Should().NotBe(0UL);
    }

    [Fact]
    public void ResolveAfterId_ExistingCheckpoint_ReturnsThatId()
    {
        HoneypotReconciliationEngine.ResolveAfterId(123456789UL, DateTime.UtcNow)
            .Should().Be(123456789UL);
    }

    [Fact]
    public void SnowflakeFromTimestamp_RoundTripsThroughDiscordUtilities()
    {
        DateTime utc = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

        ulong snowflake = HoneypotReconciliationEngine.SnowflakeFromTimestamp(utc);
        DateTimeOffset recovered = DSharpPlus.Utilities.GetSnowflakeTime(snowflake);

        recovered.UtcDateTime.Should().BeCloseTo(utc, TimeSpan.FromMilliseconds(2));
    }

    [Fact]
    public void AdvanceCheckpoint_AllSuccess_MovesToLastMessage()
    {
        ulong? next = HoneypotReconciliationEngine.AdvanceCheckpoint(
            100UL,
            [(101UL, true), (102UL, true), (103UL, true)]);

        next.Should().Be(103UL);
    }

    [Fact]
    public void AdvanceCheckpoint_FailureStopsAdvancement()
    {
        ulong? next = HoneypotReconciliationEngine.AdvanceCheckpoint(
            100UL,
            [(101UL, true), (102UL, false), (103UL, true)]);

        next.Should().Be(101UL);
    }

    [Fact]
    public void AdvanceCheckpoint_FirstFailure_KeepsPrevious()
    {
        ulong? next = HoneypotReconciliationEngine.AdvanceCheckpoint(
            100UL,
            [(101UL, false)]);

        next.Should().Be(100UL);
    }

    [Fact]
    public void AdvanceCheckpoint_NoPreviousAndFirstFailure_ReturnsNull()
    {
        ulong? next = HoneypotReconciliationEngine.AdvanceCheckpoint(
            null,
            [(101UL, false)]);

        next.Should().BeNull();
    }

    [Fact]
    public void AdvanceCheckpoint_NoPreviousAndSuccess_UsesFirstSuccessfulId()
    {
        ulong? next = HoneypotReconciliationEngine.AdvanceCheckpoint(
            null,
            [(50UL, true), (51UL, true)]);

        next.Should().Be(51UL);
    }

    [Fact]
    public void AdvanceCheckpoint_EmptyEvaluations_KeepsCurrent()
    {
        HoneypotReconciliationEngine.AdvanceCheckpoint(77UL, [])
            .Should().Be(77UL);
    }

    [Fact]
    public void OrderOldestFirst_SortsByIdAscending()
    {
        (ulong Id, string Name)[] newestFirst = [(30UL, "c"), (10UL, "a"), (20UL, "b")];

        IReadOnlyList<(ulong Id, string Name)> ordered =
            HoneypotReconciliationEngine.OrderOldestFirst(newestFirst, static x => x.Id);

        ordered.Select(x => x.Id).Should().Equal(10UL, 20UL, 30UL);
    }
}
