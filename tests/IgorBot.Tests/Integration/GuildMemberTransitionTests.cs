using FluentAssertions;

using IgorBot.Schema;

using MongoDB.Entities;

namespace IgorBot.Tests.Integration;

[Xunit.Collection("Mongo")]
public sealed class GuildMemberTransitionTests : IAsyncLifetime
{
    private readonly MongoFixture _mongo;
    private DB _db = null!;

    public GuildMemberTransitionTests(MongoFixture mongo) => _mongo = mongo;

    public async Task InitializeAsync() =>
        _db = await _mongo.CreateDatabaseAsync($"igor-transition-{Guid.NewGuid():N}");

    public Task DisposeAsync() => Task.CompletedTask;

    // ─── Happy-path: every terminal target status ────────────────────────────

    [Theory]
    [InlineData(MemberStatus.LeftVoluntarily)]
    [InlineData(MemberStatus.KickedByModerator)]
    [InlineData(MemberStatus.KickedExternally)]
    [InlineData(MemberStatus.BannedByModerator)]
    [InlineData(MemberStatus.BannedByHoneypot)]
    [InlineData(MemberStatus.BannedExternally)]
    [InlineData(MemberStatus.AutoKicked)]
    [InlineData(MemberStatus.FullMember)]
    [InlineData(MemberStatus.StrangerRoleRemoved)]
    [InlineData(MemberStatus.Onboarding)]
    [InlineData(MemberStatus.QuestionnaireSubmitted)]
    public async Task TransitionToAsync_AnyTarget_UpdatesCanonicalFieldsInDbAndMemory(MemberStatus target)
    {
        GuildMember member = await InsertFreshMember();
        DateTime before = DateTime.UtcNow;
        const string reason = "test-reason";
        const ulong actorId = 42UL;

        await member.TransitionToAsync(_db, target, reason: reason, actorId: actorId);

        // In-memory assertions
        member.Status.Should().Be(target);
        member.StatusReason.Should().Be(reason);
        member.StatusChangedAt.Should().BeOnOrAfter(before);

        // DB read-back
        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(target);
        loaded.StatusReason.Should().Be(reason);
        loaded.StatusChangedAt.Should().BeCloseTo(member.StatusChangedAt!.Value, TimeSpan.FromSeconds(2));

        // Exactly one history event
        loaded.StatusHistory.Should().ContainSingle();
        MemberStatusEvent evt = loaded.StatusHistory[0];
        evt.From.Should().Be(MemberStatus.New);
        evt.To.Should().Be(target);
        evt.Reason.Should().Be(reason);
        evt.ActorId.Should().Be(actorId);
    }

    // ─── Legacy timestamp fields per status ──────────────────────────────────

    [Fact]
    public async Task TransitionToAsync_LeftVoluntarily_SetsLeftAtClearsOthers()
    {
        GuildMember member = await InsertFreshMember();
        await member.TransitionToAsync(_db, MemberStatus.LeftVoluntarily);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded!.LeftAt.Should().NotBeNull();
        loaded.KickedAt.Should().BeNull();
        loaded.BannedAt.Should().BeNull();
        loaded.AutoKickedAt.Should().BeNull();
        loaded.PromotedAt.Should().BeNull();
        loaded.RemovedByModeration.Should().BeFalse();
    }

    [Theory]
    [InlineData(MemberStatus.KickedByModerator)]
    [InlineData(MemberStatus.KickedExternally)]
    public async Task TransitionToAsync_KickStatuses_SetsKickedAtAndRemovedByModeration(MemberStatus target)
    {
        GuildMember member = await InsertFreshMember();
        await member.TransitionToAsync(_db, target);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded!.KickedAt.Should().NotBeNull();
        loaded.RemovedByModeration.Should().BeTrue();
        loaded.BannedAt.Should().BeNull();
        loaded.LeftAt.Should().BeNull();
    }

    [Theory]
    [InlineData(MemberStatus.BannedByModerator)]
    [InlineData(MemberStatus.BannedByHoneypot)]
    [InlineData(MemberStatus.BannedExternally)]
    public async Task TransitionToAsync_BanStatuses_SetsBannedAtAndRemovedByModeration(MemberStatus target)
    {
        GuildMember member = await InsertFreshMember();
        await member.TransitionToAsync(_db, target);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded!.BannedAt.Should().NotBeNull();
        loaded.RemovedByModeration.Should().BeTrue();
        loaded.KickedAt.Should().BeNull();
        loaded.LeftAt.Should().BeNull();
    }

    [Fact]
    public async Task TransitionToAsync_AutoKicked_SetsAutoKickedAt()
    {
        GuildMember member = await InsertFreshMember();
        await member.TransitionToAsync(_db, MemberStatus.AutoKicked);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded!.AutoKickedAt.Should().NotBeNull();
        loaded.KickedAt.Should().BeNull();
        loaded.BannedAt.Should().BeNull();
    }

    [Fact]
    public async Task TransitionToAsync_FullMember_SetsPromotedAtAndFullMemberAt()
    {
        GuildMember member = await InsertFreshMember();
        await member.TransitionToAsync(_db, MemberStatus.FullMember);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded!.PromotedAt.Should().NotBeNull();
        loaded.FullMemberAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TransitionToAsync_StrangerRoleRemoved_SetsStrangerRoleRemovedAt()
    {
        GuildMember member = await InsertFreshMember();
        await member.TransitionToAsync(_db, MemberStatus.StrangerRoleRemoved);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded!.StrangerRoleRemovedAt.Should().NotBeNull();
    }

    // ─── In-memory mirrors DB ────────────────────────────────────────────────

    [Fact]
    public async Task TransitionToAsync_InMemoryStateMatchesDbReadBack()
    {
        GuildMember member = await InsertFreshMember();
        await member.TransitionToAsync(_db, MemberStatus.BannedByModerator, reason: "test");

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded!.Status.Should().Be(member.Status);
        loaded.BannedAt.Should().NotBeNull();
        loaded.RemovedByModeration.Should().Be(member.RemovedByModeration);
        member.BannedAt.Should().NotBeNull();
        member.RemovedByModeration.Should().BeTrue();
    }

    // ─── No-op when status is unchanged ─────────────────────────────────────

    [Fact]
    public async Task TransitionToAsync_SameStatus_IsNoOp_NoHistoryEntry()
    {
        GuildMember member = await InsertFreshMember();
        // status is already New
        await member.TransitionToAsync(_db, MemberStatus.New);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded!.StatusHistory.Should().BeEmpty();
        loaded.Status.Should().Be(MemberStatus.New);
    }

    // ─── Rollback path: stale fields are cleared ─────────────────────────────

    [Fact]
    public async Task TransitionToAsync_Rollback_BannedByModerator_ToOnboarding_ClearsBannedFields()
    {
        GuildMember member = await InsertFreshMember();
        await member.TransitionToAsync(_db, MemberStatus.BannedByModerator, reason: "original ban");
        await member.TransitionToAsync(_db, MemberStatus.Onboarding, reason: "rollback");

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded!.Status.Should().Be(MemberStatus.Onboarding);
        loaded.BannedAt.Should().BeNull();
        loaded.RemovedByModeration.Should().BeFalse();

        // In-memory should also be cleared
        member.BannedAt.Should().BeNull();
        member.RemovedByModeration.Should().BeFalse();
    }

    [Fact]
    public async Task TransitionToAsync_Rollback_KickedByModerator_ToOnboarding_ClearsKickFields()
    {
        GuildMember member = await InsertFreshMember();
        await member.TransitionToAsync(_db, MemberStatus.KickedByModerator);
        await member.TransitionToAsync(_db, MemberStatus.Onboarding, reason: "rollback after failed kick");

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded!.KickedAt.Should().BeNull();
        loaded.RemovedByModeration.Should().BeFalse();
    }

    // ─── History accumulates across multiple transitions ─────────────────────

    [Fact]
    public async Task TransitionToAsync_MultipleTransitions_HistoryPreservesAll()
    {
        GuildMember member = await InsertFreshMember();
        await member.TransitionToAsync(_db, MemberStatus.Onboarding);
        await member.TransitionToAsync(_db, MemberStatus.QuestionnaireSubmitted);
        await member.TransitionToAsync(_db, MemberStatus.FullMember);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded!.StatusHistory.Should().HaveCount(3);
        loaded.StatusHistory[0].To.Should().Be(MemberStatus.Onboarding);
        loaded.StatusHistory[1].To.Should().Be(MemberStatus.QuestionnaireSubmitted);
        loaded.StatusHistory[2].To.Should().Be(MemberStatus.FullMember);
    }

    // ─── Concurrent-safety smoke test ────────────────────────────────────────

    [Fact]
    public async Task TransitionToAsync_ConcurrentCalls_BothHistoryEntriesLandInDb()
    {
        // Two concurrent callers each read the same document and call TransitionToAsync
        // to different targets simultaneously. Both $push operations must land because
        // TransitionToAsync uses Update<T>().Modify($push) rather than a full-document save.
        GuildMember member = await InsertFreshMember();

        // Read the same doc twice (simulating two concurrent handlers)
        GuildMember? copy1 = await _db.Find<GuildMember>().OneAsync(member.ID);
        GuildMember? copy2 = await _db.Find<GuildMember>().OneAsync(member.ID);
        copy1.Should().NotBeNull();
        copy2.Should().NotBeNull();

        await Task.WhenAll(
            copy1!.TransitionToAsync(_db, MemberStatus.Onboarding, reason: "copy1"),
            copy2!.TransitionToAsync(_db, MemberStatus.QuestionnaireSubmitted, reason: "copy2")
        );

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        // Both $push operations must have landed — regardless of which $set won.
        loaded!.StatusHistory.Should().HaveCount(2);
        loaded.StatusHistory.Select(e => e.To).Should()
            .Contain(MemberStatus.Onboarding).And
            .Contain(MemberStatus.QuestionnaireSubmitted);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<GuildMember> InsertFreshMember()
    {
        string memberId = Guid.NewGuid().ToString("N")[..12];
        GuildMember member = new()
        {
            GuildId = 1UL,
            MemberId = ulong.Parse(memberId, System.Globalization.NumberStyles.HexNumber),
            Member = $"user-{memberId}",
            Mention = $"<@{memberId}>",
            ID = $"1-{memberId}",
            Status = MemberStatus.New
        };
        await _db.SaveAsync(member);
        return member;
    }
}
