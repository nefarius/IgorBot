using System.Runtime.CompilerServices;

using DSharpPlus.Exceptions;

using FluentAssertions;

using IgorBot.Services;

using Microsoft.Extensions.Logging.Abstractions;

namespace IgorBot.Tests.Services;

public sealed class HoneypotMessageCleanupTests
{
    private static readonly TimeSpan OlderThanLegacyPurgeWindow = TimeSpan.FromDays(3);

    private static readonly TimeSpan OlderThanDiscordPurgeMaximum = TimeSpan.FromDays(8);

    [Fact]
    public async Task TryDeleteTriggerAsync_ImmediateBan_DeletesFreshMessage()
    {
        int deletes = 0;

        await HoneypotMessageCleanup.TryDeleteTriggerAsync(
            HoneypotEnforcementOutcome.Banned,
            TimeSpan.Zero,
            () =>
            {
                deletes++;
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            MessageId(TimeSpan.Zero));

        deletes.Should().Be(1);
    }

    [Fact]
    public Task TryDeleteTriggerAsync_ReconciledBan_DeletesMessageOlderThanOneDay() =>
        AssertDeletes(HoneypotEnforcementOutcome.Banned, OlderThanLegacyPurgeWindow);

    [Fact]
    public Task TryDeleteTriggerAsync_ReconciledAlreadyBanned_DeletesMessageOlderThanOneDay() =>
        AssertDeletes(HoneypotEnforcementOutcome.AlreadyBanned, OlderThanLegacyPurgeWindow);

    [Fact]
    public Task TryDeleteTriggerAsync_ReconciledMemberGone_DeletesMessageOlderThanOneDay() =>
        AssertDeletes(HoneypotEnforcementOutcome.MemberGone, OlderThanLegacyPurgeWindow);

    [Fact]
    public async Task TryDeleteTriggerAsync_ReconciledBan_DeletesMessageOlderThanSevenDays()
    {
        int deletes = 0;

        await HoneypotMessageCleanup.TryDeleteTriggerAsync(
            HoneypotEnforcementOutcome.Banned,
            OlderThanDiscordPurgeMaximum,
            () =>
            {
                deletes++;
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            MessageId(OlderThanDiscordPurgeMaximum));

        deletes.Should().Be(1);
    }

    [Fact]
    public Task TryDeleteTriggerAsync_FailedEnforcement_DoesNotDelete() =>
        AssertDoesNotDelete(HoneypotEnforcementOutcome.Failed);

    [Fact]
    public Task TryDeleteTriggerAsync_PermanentFailure_DoesNotDelete() =>
        AssertDoesNotDelete(HoneypotEnforcementOutcome.PermanentFailure);

    [Fact]
    public async Task TryDeleteTriggerAsync_DeleteFailure_DoesNotThrowOrRetry()
    {
        int deletes = 0;

        Func<Task> act = () => HoneypotMessageCleanup.TryDeleteTriggerAsync(
            HoneypotEnforcementOutcome.Banned,
            OlderThanLegacyPurgeWindow,
            () =>
            {
                deletes++;
                throw new InvalidOperationException("missing manage messages");
            },
            NullLogger.Instance,
            MessageId(OlderThanLegacyPurgeWindow));

        await act.Should().NotThrowAsync();
        deletes.Should().Be(1);
    }

    [Fact]
    public void DecideCheckpointDeletion_ConfirmedBan_Deletes()
    {
        HoneypotMessageCleanup.DecideCheckpointDeletion(HoneypotEligibility.Enforce, banConfirmed: true)
            .Should().Be(CheckpointDeletion.Delete);
    }

    [Fact]
    public void DecideCheckpointDeletion_ExemptAuthor_LeavesMessage()
    {
        HoneypotMessageCleanup.DecideCheckpointDeletion(HoneypotEligibility.SkipOwner, banConfirmed: true)
            .Should().Be(CheckpointDeletion.Leave);
        HoneypotMessageCleanup.DecideCheckpointDeletion(HoneypotEligibility.SkipExcludedRole, banConfirmed: true)
            .Should().Be(CheckpointDeletion.Leave);
        HoneypotMessageCleanup.DecideCheckpointDeletion(HoneypotEligibility.SkipBot, banConfirmed: true)
            .Should().Be(CheckpointDeletion.Leave);
    }

    [Fact]
    public void DecideCheckpointDeletion_NoBan_LeavesMessage()
    {
        HoneypotMessageCleanup.DecideCheckpointDeletion(HoneypotEligibility.Enforce, banConfirmed: false)
            .Should().Be(CheckpointDeletion.Leave);
    }

    [Fact]
    public void DecideCheckpointDeletion_UnconfirmedBan_Retries()
    {
        HoneypotMessageCleanup.DecideCheckpointDeletion(HoneypotEligibility.Enforce, banConfirmed: null)
            .Should().Be(CheckpointDeletion.Retry);
    }

    [Fact]
    public async Task TryDeleteCheckpointedMessageAsync_ConfirmedDeletion_ReturnsTrue()
    {
        int deletes = 0;
        ulong messageId = MessageId(OlderThanLegacyPurgeWindow);

        bool deleted = await HoneypotMessageCleanup.TryDeleteCheckpointedMessageAsync(
            () =>
            {
                deletes++;
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            messageId);

        deleted.Should().BeTrue();
        deletes.Should().Be(1);
        messageId.Should().BeLessThan(MessageId(TimeSpan.FromDays(1)));
    }

    [Fact]
    public async Task TryDeleteCheckpointedMessageAsync_AlreadyDeleted_ReturnsTrue()
    {
        bool deleted = await HoneypotMessageCleanup.TryDeleteCheckpointedMessageAsync(
            () => throw Uninitialized<NotFoundException>(),
            NullLogger.Instance,
            MessageId(OlderThanLegacyPurgeWindow));

        deleted.Should().BeTrue();
    }

    [Fact]
    public async Task TryDeleteCheckpointedMessageAsync_DeleteFailure_ReturnsFalse()
    {
        bool deleted = await HoneypotMessageCleanup.TryDeleteCheckpointedMessageAsync(
            () => throw new InvalidOperationException("missing manage messages"),
            NullLogger.Instance,
            MessageId(OlderThanLegacyPurgeWindow));

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task TryDeleteTriggerAsync_AlreadyDeleted_DoesNotThrow()
    {
        Func<Task> act = () => HoneypotMessageCleanup.TryDeleteTriggerAsync(
            HoneypotEnforcementOutcome.AlreadyBanned,
            OlderThanLegacyPurgeWindow,
            () => throw Uninitialized<NotFoundException>(),
            NullLogger.Instance,
            MessageId(OlderThanLegacyPurgeWindow));

        await act.Should().NotThrowAsync();
    }

    private static async Task AssertDeletes(HoneypotEnforcementOutcome outcome, TimeSpan age)
    {
        int deletes = 0;
        ulong messageId = MessageId(age);

        await HoneypotMessageCleanup.TryDeleteTriggerAsync(
            outcome,
            age,
            () =>
            {
                deletes++;
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            messageId);

        deletes.Should().Be(1);
        messageId.Should().BeLessThan(MessageId(TimeSpan.FromDays(1)));
    }

    private static async Task AssertDoesNotDelete(HoneypotEnforcementOutcome outcome)
    {
        int deletes = 0;

        await HoneypotMessageCleanup.TryDeleteTriggerAsync(
            outcome,
            OlderThanLegacyPurgeWindow,
            () =>
            {
                deletes++;
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            MessageId(OlderThanLegacyPurgeWindow));

        deletes.Should().Be(0);
    }

    private static ulong MessageId(TimeSpan age) =>
        HoneypotReconciliationEngine.SnowflakeFromTimestamp(DateTime.UtcNow - age);

    private static Exception Uninitialized<T>() where T : Exception =>
        (Exception)RuntimeHelpers.GetUninitializedObject(typeof(T));
}
