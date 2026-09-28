using FluentAssertions;

using IgorBot.Schema;
using IgorBot.Services;

using MongoDB.Entities;

namespace IgorBot.Tests.Integration;

[Xunit.Collection("Mongo")]
public sealed class HoneypotReconciliationStateTests : IAsyncLifetime
{
    private readonly MongoFixture _mongo;
    private DB _db = null!;

    public HoneypotReconciliationStateTests(MongoFixture mongo) => _mongo = mongo;

    public async Task InitializeAsync() =>
        _db = await _mongo.CreateDatabaseAsync($"igor-honeypot-cursor-{Guid.NewGuid():N}");

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SaveAndReload_PersistsCheckpointPerGuildAndChannel()
    {
        HoneypotReconciliationState state = new()
        {
            GuildId = 1,
            ChannelId = 2,
            LastProcessedMessageId = 99,
            LastReconciledAt = DateTime.UtcNow,
            ID = "1-2"
        };
        await _db.SaveAsync(state);

        HoneypotReconciliationState? loaded = await _db.Find<HoneypotReconciliationState>().OneAsync("1-2");
        loaded.Should().NotBeNull();
        loaded!.LastProcessedMessageId.Should().Be(99UL);
        loaded.GuildId.Should().Be(1UL);
        loaded.ChannelId.Should().Be(2UL);
    }

    [Fact]
    public async Task ChangingChannel_CreatesIndependentCursor()
    {
        await _db.SaveAsync(new HoneypotReconciliationState
        {
            GuildId = 1,
            ChannelId = 10,
            LastProcessedMessageId = 100,
            ID = "1-10"
        });
        await _db.SaveAsync(new HoneypotReconciliationState
        {
            GuildId = 1,
            ChannelId = 20,
            LastProcessedMessageId = 200,
            ID = "1-20"
        });

        HoneypotReconciliationState? original =
            await _db.Find<HoneypotReconciliationState>().OneAsync("1-10");
        HoneypotReconciliationState? moved =
            await _db.Find<HoneypotReconciliationState>().OneAsync("1-20");

        original!.LastProcessedMessageId.Should().Be(100UL);
        moved!.LastProcessedMessageId.Should().Be(200UL);
    }

    [Fact]
    public async Task Rerun_IsIdempotentWhenCheckpointUnchanged()
    {
        DateTime now = DateTime.UtcNow;
        ulong checkpoint = HoneypotReconciliationEngine.SnowflakeFromTimestamp(now.AddMinutes(-5));
        HoneypotReconciliationState state = new()
        {
            GuildId = 5,
            ChannelId = 6,
            LastProcessedMessageId = checkpoint,
            ID = "5-6"
        };
        await _db.SaveAsync(state);

        ulong afterId = HoneypotReconciliationEngine.ResolveAfterId(checkpoint, now);
        afterId.Should().Be(checkpoint);

        ulong? next = HoneypotReconciliationEngine.AdvanceCheckpoint(afterId, []);
        next.Should().Be(checkpoint);

        state.LastProcessedMessageId = next.Value;
        await _db.SaveAsync(state);

        HoneypotReconciliationState? loaded = await _db.Find<HoneypotReconciliationState>().OneAsync("5-6");
        loaded!.LastProcessedMessageId.Should().Be(checkpoint);
    }

    [Fact]
    public async Task FailedEvaluation_DoesNotAdvancePersistedCheckpoint()
    {
        await _db.SaveAsync(new HoneypotReconciliationState
        {
            GuildId = 8,
            ChannelId = 9,
            LastProcessedMessageId = 10,
            ID = "8-9"
        });

        ulong? next = HoneypotReconciliationEngine.AdvanceCheckpoint(
            10UL,
            [(11UL, true), (12UL, false)]);

        next.Should().Be(11UL);

        HoneypotReconciliationState persisted = (await _db.Find<HoneypotReconciliationState>().OneAsync("8-9"))!;
        persisted.LastProcessedMessageId = next.Value;
        await _db.SaveAsync(persisted);

        HoneypotReconciliationState? loaded = await _db.Find<HoneypotReconciliationState>().OneAsync("8-9");
        loaded!.LastProcessedMessageId.Should().Be(11UL);
        loaded.LastProcessedMessageId.Should().NotBe(12UL);
    }

    [Fact]
    public async Task DisconnectedScan_PersistsContinuationWithoutAdvancingCheckpoint()
    {
        await _db.SaveAsync(new HoneypotReconciliationState
        {
            GuildId = 3,
            ChannelId = 4,
            LastProcessedMessageId = 10,
            ID = "3-4"
        });

        bool connected = HoneypotReconciliationEngine.IsConnectedToCursor(10, 500, reachedHistoryStart: false);
        ulong? continuation = HoneypotReconciliationEngine.ContinuationBeforeId(connected, 500);
        connected.Should().BeFalse();

        HoneypotReconciliationState persisted =
            (await _db.Find<HoneypotReconciliationState>().OneAsync("3-4"))!;
        persisted.ContinuationBeforeMessageId = continuation;
        await _db.SaveAsync(persisted);

        HoneypotReconciliationState? loaded = await _db.Find<HoneypotReconciliationState>().OneAsync("3-4");
        loaded!.LastProcessedMessageId.Should().Be(10UL);
        loaded.ContinuationBeforeMessageId.Should().Be(500UL);
    }
}
