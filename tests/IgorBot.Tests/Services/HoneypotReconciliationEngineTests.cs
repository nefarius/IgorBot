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
    public void ResolveAfterId_NewerCheckpoint_IsPreserved()
    {
        DateTime now = new(2026, 9, 28, 16, 0, 0, DateTimeKind.Utc);
        ulong checkpoint = HoneypotReconciliationEngine.SnowflakeFromTimestamp(now.AddHours(-1));

        HoneypotReconciliationEngine.ResolveAfterId(checkpoint, now)
            .Should().Be(checkpoint);
    }

    [Fact]
    public void ResolveAfterId_RecentActivation_StartsAtActivationNotFullLookback()
    {
        DateTime now = new(2026, 9, 28, 16, 0, 0, DateTimeKind.Utc);
        DateTime activated = now.AddHours(-2);

        ulong afterId = HoneypotReconciliationEngine.ResolveAfterId(null, now, activated);

        afterId.Should().Be(HoneypotReconciliationEngine.SnowflakeFromTimestamp(activated));
        afterId.Should().NotBe(HoneypotReconciliationEngine.SnowflakeFromTimestamp(
            now - HoneypotReconciliationEngine.InitialLookback));
    }

    [Fact]
    public void ResolveAfterId_OldActivation_UsesLookbackWindow()
    {
        DateTime now = new(2026, 9, 28, 16, 0, 0, DateTimeKind.Utc);
        DateTime activated = now.AddDays(-3);

        ulong afterId = HoneypotReconciliationEngine.ResolveAfterId(null, now, activated);

        afterId.Should().Be(HoneypotReconciliationEngine.SnowflakeFromTimestamp(
            now - HoneypotReconciliationEngine.InitialLookback));
    }

    [Fact]
    public void ResolveAfterId_NewerCheckpoint_TakesPrecedenceOverActivation()
    {
        DateTime now = new(2026, 9, 28, 16, 0, 0, DateTimeKind.Utc);
        DateTime activated = now.AddHours(-2);
        ulong checkpoint = HoneypotReconciliationEngine.SnowflakeFromTimestamp(now.AddHours(-1));

        HoneypotReconciliationEngine.ResolveAfterId(checkpoint, now, activated)
            .Should().Be(checkpoint);
    }

    [Fact]
    public void ResolveAfterId_CheckpointOlderThanActivation_UsesActivationBoundary()
    {
        DateTime now = new(2026, 9, 28, 16, 0, 0, DateTimeKind.Utc);
        DateTime activated = now.AddHours(-2);
        ulong staleCheckpoint = HoneypotReconciliationEngine.SnowflakeFromTimestamp(activated.AddHours(-3));

        ulong afterId = HoneypotReconciliationEngine.ResolveAfterId(staleCheckpoint, now, activated);

        afterId.Should().Be(HoneypotReconciliationEngine.SnowflakeFromTimestamp(activated));
        afterId.Should().BeGreaterThan(staleCheckpoint);
    }

    [Fact]
    public void ResolveAfterId_ExistingCheckpointOlderThanLookback_IsPreserved()
    {
        DateTime now = new(2026, 9, 28, 16, 0, 0, DateTimeKind.Utc);
        ulong checkpoint = HoneypotReconciliationEngine.SnowflakeFromTimestamp(now.AddHours(-36));

        HoneypotReconciliationEngine.ResolveAfterId(checkpoint, now)
            .Should().Be(checkpoint);
        HoneypotReconciliationEngine.ResolveAfterId(checkpoint, now)
            .Should().BeLessThan(HoneypotReconciliationEngine.SnowflakeFromTimestamp(
                now - HoneypotReconciliationEngine.InitialLookback));
    }

    [Fact]
    public void IsConnectedToCursor_ReachedHistoryStart_IsConnected()
    {
        HoneypotReconciliationEngine.IsConnectedToCursor(10, 50, reachedHistoryStart: true)
            .Should().BeTrue();
    }

    [Fact]
    public void IsConnectedToCursor_OldestAtOrBeforeCursor_IsConnected()
    {
        HoneypotReconciliationEngine.IsConnectedToCursor(10, 10, reachedHistoryStart: false)
            .Should().BeTrue();
        HoneypotReconciliationEngine.IsConnectedToCursor(10, 9, reachedHistoryStart: false)
            .Should().BeTrue();
    }

    [Fact]
    public void IsConnectedToCursor_GapRemains_IsNotConnected()
    {
        HoneypotReconciliationEngine.IsConnectedToCursor(10, 50, reachedHistoryStart: false)
            .Should().BeFalse();
    }

    [Fact]
    public void ContinuationBeforeId_Disconnected_KeepsOldestFetched()
    {
        HoneypotReconciliationEngine.ContinuationBeforeId(connected: false, 50)
            .Should().Be(50UL);
    }

    [Fact]
    public void ContinuationBeforeId_Connected_ClearsContinuation()
    {
        HoneypotReconciliationEngine.ContinuationBeforeId(connected: true, 50)
            .Should().BeNull();
    }

    [Fact]
    public void SnowflakeFromTimestamp_RoundTripsThroughDiscordUtilities()
    {
        DateTime utc = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

        ulong snowflake = HoneypotReconciliationEngine.SnowflakeFromTimestamp(utc);
        DateTimeOffset recovered = HoneypotReconciliationEngine.TimestampFromSnowflake(snowflake);

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

    [Fact]
    public void SelectBacklogMessageIds_IncludesAgedMessageBehindCheckpoint()
    {
        const ulong agedMessageId = 1559668871739478132UL;
        ulong ownerEraMessageId = HoneypotReconciliationEngine.SnowflakeFromTimestamp(
            new DateTime(2024, 6, 24, 19, 48, 0, DateTimeKind.Utc));
        ulong checkpoint = agedMessageId;

        IReadOnlyList<ulong> selected = HoneypotReconciliationEngine.SelectBacklogMessageIds(
            [ownerEraMessageId, agedMessageId, checkpoint + 10UL],
            checkpoint,
            lowerBound: 0);

        selected.Should().Equal(agedMessageId, ownerEraMessageId);
        HoneypotReconciliationEngine.BeforeIdIncluding(checkpoint).Should().BeGreaterThan(agedMessageId);
    }

    [Fact]
    public void SelectBacklogMessageIds_ActivationExcludesOlderMessages()
    {
        const ulong agedMessageId = 1559668871739478132UL;
        ulong ownerEraMessageId = HoneypotReconciliationEngine.SnowflakeFromTimestamp(
            new DateTime(2024, 6, 24, 19, 48, 0, DateTimeKind.Utc));
        ulong lowerBound = HoneypotReconciliationEngine.BacklogLowerBound(
            new DateTime(2026, 9, 28, 16, 55, 0, DateTimeKind.Utc));

        IReadOnlyList<ulong> selected = HoneypotReconciliationEngine.SelectBacklogMessageIds(
            [ownerEraMessageId, agedMessageId],
            checkpoint: agedMessageId,
            lowerBound);

        selected.Should().Equal(agedMessageId);
        HoneypotReconciliationEngine.IsBacklogMessage(ownerEraMessageId, agedMessageId, lowerBound)
            .Should().BeFalse();
    }

    [Fact]
    public void IsBacklogMessage_CheckpointMessage_IsIncluded()
    {
        HoneypotReconciliationEngine.IsBacklogMessage(40, checkpoint: 40, lowerBound: 10)
            .Should().BeTrue();
    }

    [Fact]
    public void IsBacklogMessage_AtLowerBound_IsExcluded()
    {
        HoneypotReconciliationEngine.IsBacklogMessage(10, checkpoint: 40, lowerBound: 10)
            .Should().BeFalse();
    }

    [Fact]
    public void IsFinalHistoryPage_ShortPage_IsFinal()
    {
        HoneypotReconciliationEngine.IsFinalHistoryPage(0).Should().BeTrue();
        HoneypotReconciliationEngine.IsFinalHistoryPage(HoneypotReconciliationEngine.PageSize - 1).Should().BeTrue();
    }

    [Fact]
    public void IsFinalHistoryPage_FullPage_Continues()
    {
        HoneypotReconciliationEngine.IsFinalHistoryPage(HoneypotReconciliationEngine.PageSize).Should().BeFalse();
    }

    [Fact]
    public void BeforeIdIncluding_RetriesTheSameMessage()
    {
        HoneypotReconciliationEngine.BeforeIdIncluding(40).Should().Be(41UL);
        HoneypotReconciliationEngine.BeforeIdIncluding(ulong.MaxValue).Should().Be(ulong.MaxValue);
    }

    [Fact]
    public void BacklogLowerBound_MissingActivation_IncludesEntireHistory()
    {
        HoneypotReconciliationEngine.BacklogLowerBound(null).Should().Be(0UL);
    }
}
