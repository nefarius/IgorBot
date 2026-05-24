using FluentAssertions;

using IgorBot.Schema;
using IgorBot.Services;

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
}
