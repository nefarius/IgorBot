using FluentAssertions;

using IgorBot.Schema;
using IgorBot.Services;

using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Entities;

namespace IgorBot.Tests.Integration;

[Xunit.Collection("Mongo")]
public sealed class GuildMemberStatusMigrationDbTests : IAsyncLifetime
{
    private readonly MongoFixture _mongo;
    private DB _db = null!;

    public GuildMemberStatusMigrationDbTests(MongoFixture mongo) => _mongo = mongo;

    public async Task InitializeAsync() =>
        _db = await _mongo.CreateDatabaseAsync($"igor-migration-{Guid.NewGuid():N}");

    public Task DisposeAsync() => Task.CompletedTask;

    // ─── Dry-run writes nothing ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DryRun_DoesNotWriteToDatabase()
    {
        GuildMember m = await InsertUnmigrated(bannedAt: DateTime.UtcNow.AddHours(-1));

        await GuildMemberStatusMigration.RunAsync(_db, dryRun: true);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(m.ID);
        loaded!.Status.Should().Be(MemberStatus.Unknown, because: "dry-run must not mutate documents");
        loaded.StatusHistory.Should().BeEmpty(because: "dry-run must not append history");
    }

    // ─── Correct status derivation for every legacy shape ────────────────────

    [Fact]
    public async Task RunAsync_BannedAt_WithRemovedByModeration_MigratesTo_BannedByModerator()
    {
        GuildMember m = await InsertUnmigrated(bannedAt: DateTime.UtcNow.AddHours(-1), removedByModeration: true);

        await GuildMemberStatusMigration.RunAsync(_db);

        (await Reload(m)).Status.Should().Be(MemberStatus.BannedByModerator);
    }

    [Fact]
    public async Task RunAsync_BannedAt_WithoutRemovedByModeration_MigratesTo_BannedExternally()
    {
        GuildMember m = await InsertUnmigrated(bannedAt: DateTime.UtcNow.AddHours(-1));

        await GuildMemberStatusMigration.RunAsync(_db);

        (await Reload(m)).Status.Should().Be(MemberStatus.BannedExternally);
    }

    [Fact]
    public async Task RunAsync_KickedAt_WithRemovedByModeration_MigratesTo_KickedByModerator()
    {
        GuildMember m = await InsertUnmigrated(kickedAt: DateTime.UtcNow.AddHours(-1), removedByModeration: true);

        await GuildMemberStatusMigration.RunAsync(_db);

        (await Reload(m)).Status.Should().Be(MemberStatus.KickedByModerator);
    }

    [Fact]
    public async Task RunAsync_KickedAt_WithoutRemovedByModeration_MigratesTo_KickedExternally()
    {
        GuildMember m = await InsertUnmigrated(kickedAt: DateTime.UtcNow.AddHours(-1));

        await GuildMemberStatusMigration.RunAsync(_db);

        (await Reload(m)).Status.Should().Be(MemberStatus.KickedExternally);
    }

    [Fact]
    public async Task RunAsync_AutoKickedAt_MigratesTo_AutoKicked()
    {
        GuildMember m = await InsertUnmigrated(autoKickedAt: DateTime.UtcNow.AddHours(-1));

        await GuildMemberStatusMigration.RunAsync(_db);

        (await Reload(m)).Status.Should().Be(MemberStatus.AutoKicked);
    }

    [Fact]
    public async Task RunAsync_LeftAt_MigratesTo_LeftVoluntarily()
    {
        GuildMember m = await InsertUnmigrated(leftAt: DateTime.UtcNow.AddHours(-1));

        await GuildMemberStatusMigration.RunAsync(_db);

        (await Reload(m)).Status.Should().Be(MemberStatus.LeftVoluntarily);
    }

    [Fact]
    public async Task RunAsync_PromotedAt_MigratesTo_FullMember()
    {
        GuildMember m = await InsertUnmigrated(promotedAt: DateTime.UtcNow.AddHours(-1));

        await GuildMemberStatusMigration.RunAsync(_db);

        (await Reload(m)).Status.Should().Be(MemberStatus.FullMember);
    }

    [Fact]
    public async Task RunAsync_NoTimestamps_MigratesTo_New()
    {
        GuildMember m = await InsertUnmigrated();

        await GuildMemberStatusMigration.RunAsync(_db);

        (await Reload(m)).Status.Should().Be(MemberStatus.New);
    }

    // ─── History event appended ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AppendsOneMigrationHistoryEvent_WithReasonMigration()
    {
        GuildMember m = await InsertUnmigrated(leftAt: DateTime.UtcNow.AddHours(-1));

        await GuildMemberStatusMigration.RunAsync(_db);

        GuildMember loaded = await Reload(m);
        loaded.StatusHistory.Should().ContainSingle();
        MemberStatusEvent evt = loaded.StatusHistory[0];
        evt.From.Should().Be(MemberStatus.Unknown);
        evt.To.Should().Be(MemberStatus.LeftVoluntarily);
        evt.Reason.Should().Be("migration");
    }

    // ─── RemovedByModeration is set for mod-terminal statuses ────────────────

    [Fact]
    public async Task RunAsync_ModerationTerminalStatus_SetsRemovedByModeration()
    {
        GuildMember m = await InsertUnmigrated(kickedAt: DateTime.UtcNow.AddHours(-1), removedByModeration: true);

        await GuildMemberStatusMigration.RunAsync(_db);

        (await Reload(m)).RemovedByModeration.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_NonModerationStatus_DoesNotSetRemovedByModeration()
    {
        GuildMember m = await InsertUnmigrated(leftAt: DateTime.UtcNow.AddHours(-1));

        await GuildMemberStatusMigration.RunAsync(_db);

        (await Reload(m)).RemovedByModeration.Should().BeFalse();
    }

    // ─── Idempotency ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CalledTwice_IsIdempotent_NoExtraHistoryEntries()
    {
        GuildMember m = await InsertUnmigrated(leftAt: DateTime.UtcNow.AddHours(-1));

        await GuildMemberStatusMigration.RunAsync(_db);
        await GuildMemberStatusMigration.RunAsync(_db);

        GuildMember loaded = await Reload(m);
        loaded.StatusHistory.Should().ContainSingle(
            because: "second RunAsync must be a no-op — already migrated docs have Status != Unknown");
    }

    // ─── Already-migrated docs are not touched ───────────────────────────────

    [Fact]
    public async Task RunAsync_AlreadyMigratedDoc_IsNotModified()
    {
        GuildMember m = await InsertFresh(MemberStatus.FullMember);

        await GuildMemberStatusMigration.RunAsync(_db);

        GuildMember loaded = await Reload(m);
        loaded.Status.Should().Be(MemberStatus.FullMember);
        loaded.StatusHistory.Should().BeEmpty();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<GuildMember> InsertUnmigrated(
        DateTime? bannedAt = null,
        DateTime? kickedAt = null,
        DateTime? autoKickedAt = null,
        DateTime? leftAt = null,
        DateTime? promotedAt = null,
        bool removedByModeration = false)
    {
        string id = UniqueId();
        GuildMember m = new()
        {
            GuildId = 1UL,
            MemberId = 2UL,
            Member = $"user-{id}",
            Mention = $"<@{id}>",
            ID = id,
            Status = MemberStatus.Unknown,
            BannedAt = bannedAt,
            KickedAt = kickedAt,
            AutoKickedAt = autoKickedAt,
            LeftAt = leftAt,
            PromotedAt = promotedAt,
            RemovedByModeration = removedByModeration
        };
        await _db.SaveAsync(m);
        return m;
    }

    private async Task<GuildMember> InsertFresh(MemberStatus status)
    {
        string id = UniqueId();
        GuildMember m = new()
        {
            GuildId = 1UL,
            MemberId = 3UL,
            Member = $"user-{id}",
            Mention = $"<@{id}>",
            ID = id,
            Status = status
        };
        await _db.SaveAsync(m);
        return m;
    }

    private async Task<GuildMember> Reload(GuildMember m)
    {
        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(m.ID);
        loaded.Should().NotBeNull();
        return loaded!;
    }

    private static string UniqueId() => Guid.NewGuid().ToString("N")[..16];

    // ─── Legacy shape regression (no Status field in BSON) ───────────────────

    /// <summary>
    ///     Reproduces the true production legacy shape: a raw BSON document written before Phase 2
    ///     that has no <c>Status</c> field at all (not even <c>Status: 0</c>).
    /// </summary>
    private async Task<string> InsertLegacyRaw(
        DateTime? leftAt = null,
        DateTime? kickedAt = null,
        DateTime? bannedAt = null,
        DateTime? promotedAt = null)
    {
        string id = UniqueId();

        // Bypass MongoDB.Entities serialization so Status is genuinely absent.
        IMongoCollection<GuildMember> typedColl = _db.Collection<GuildMember>();
        IMongoCollection<BsonDocument> rawColl =
            typedColl.Database.GetCollection<BsonDocument>(typedColl.CollectionNamespace.CollectionName);

        BsonDocument doc = new()
        {
            ["_id"] = id,
            ["MemberId"] = new BsonInt64(2),
            ["GuildId"] = new BsonInt64(1),
            ["Application"] = BsonNull.Value,
            ["Channel"] = BsonNull.Value,
            ["CreatedAt"] = new BsonDateTime(DateTime.UtcNow.AddDays(-1)),
            ["JoinedAt"] = new BsonDateTime(DateTime.UtcNow.AddDays(-1)),
            ["LeftAt"] = leftAt.HasValue ? new BsonDateTime(leftAt.Value) : BsonNull.Value,
            ["PromotedAt"] = promotedAt.HasValue ? new BsonDateTime(promotedAt.Value) : BsonNull.Value,
            ["KickedAt"] = kickedAt.HasValue ? new BsonDateTime(kickedAt.Value) : BsonNull.Value,
            ["BannedAt"] = bannedAt.HasValue ? new BsonDateTime(bannedAt.Value) : BsonNull.Value,
            ["Member"] = $"Member {id}; legacy#0000 (legacy)",
            ["Mention"] = $"<@!{id}>"
            // Intentionally no "Status" key — this is the pre-Phase-2 shape
        };

        await rawColl.InsertOneAsync(doc);
        return id;
    }

    [Fact]
    public async Task RunAsync_LegacyDocWithoutStatusField_IsMigrated()
    {
        // Arrange: insert a raw legacy document that has no Status field (true production shape)
        string id = await InsertLegacyRaw(leftAt: DateTime.UtcNow.AddHours(-1));

        // Act
        await GuildMemberStatusMigration.RunAsync(_db);

        // Assert
        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(id);
        loaded.Should().NotBeNull(because: "the raw legacy document must be findable after migration");
        loaded!.Status.Should().Be(MemberStatus.LeftVoluntarily,
            because: "LeftAt is set so the member left voluntarily");
        loaded.StatusHistory.Should().ContainSingle(
            because: "migration must append exactly one history event");
        loaded.StatusHistory[0].Reason.Should().Be("migration");
    }

    // ─── Corrupted document with null _id is skipped gracefully ─────────────

    [Fact]
    public async Task RunAsync_WriteSidePredicate_DoesNotOverwriteConcurrentlyMigratedDoc()
    {
        // Arrange: doc starts with Status=Unknown so the Find picks it up.
        GuildMember m = await InsertUnmigrated(leftAt: DateTime.UtcNow.AddHours(-1));

        // Act: inject a hook that fires after Find (document is already in the candidate list)
        // but before the updateOne — this is the exact window the write-side predicate guards.
        // The hook simulates a concurrent TransitionToAsync that wins the race.
        await GuildMemberStatusMigration.RunAsync(_db, beforeUpdateHook: async () =>
        {
            await _db.Update<GuildMember>()
                .MatchID(m.ID)
                .Modify(x => x.Status, MemberStatus.FullMember)
                .ExecuteAsync();
        });

        // Assert: the write-side predicate sees Status != Unknown and makes the updateOne a
        // no-op; the concurrent winner's status and empty history are preserved.
        GuildMember loaded = await Reload(m);
        loaded.Status.Should().Be(MemberStatus.FullMember,
            because: "the write-side predicate must not overwrite a status set after the Find");
        loaded.StatusHistory.Should().BeEmpty(
            because: "no migration event must be appended when the update is a no-op");
    }

    [Fact]
    public async Task RunAsync_DocumentWithNullId_IsSkippedWithoutCrash()
    {
        // Arrange: insert a corrupted document whose _id is BsonNull — this is what the C#
        // driver deserializes to string ID = null, triggering the SaveAsync → INSERT → DupKey
        // crash that was observed in production.
        IMongoCollection<GuildMember> typedColl = _db.Collection<GuildMember>();
        IMongoCollection<BsonDocument> rawColl =
            typedColl.Database.GetCollection<BsonDocument>(typedColl.CollectionNamespace.CollectionName);

        BsonDocument corrupted = new()
        {
            ["_id"] = BsonNull.Value,
            ["MemberId"] = new BsonInt64(99),
            ["GuildId"] = new BsonInt64(1),
            ["JoinedAt"] = new BsonDateTime(DateTime.UtcNow.AddDays(-1)),
            ["LeftAt"] = new BsonDateTime(DateTime.UtcNow.AddHours(-1)),
            ["Member"] = "corrupted#0000",
            ["Mention"] = "<@!99>"
        };
        await rawColl.InsertOneAsync(corrupted);

        // Also insert one healthy legacy doc so we can verify normal migration still completes.
        string healthyId = await InsertLegacyRaw(leftAt: DateTime.UtcNow.AddHours(-2));

        // Act — must not throw
        await GuildMemberStatusMigration.RunAsync(_db);

        // Assert: the healthy document was migrated; the corrupted one was skipped.
        GuildMember? healthy = await _db.Find<GuildMember>().OneAsync(healthyId);
        healthy!.Status.Should().Be(MemberStatus.LeftVoluntarily,
            because: "healthy legacy doc must still be migrated despite the corrupted peer");
    }
}
