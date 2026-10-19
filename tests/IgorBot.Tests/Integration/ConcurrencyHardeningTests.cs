using FluentAssertions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;
using IgorBot.Util;

using MongoDB.Entities;

namespace IgorBot.Tests.Integration;

[Xunit.Collection("Mongo")]
public sealed class ConcurrencyHardeningTests : IAsyncLifetime
{
    private readonly MongoFixture _mongo;
    private DB _db = null!;

    public ConcurrencyHardeningTests(MongoFixture mongo) => _mongo = mongo;

    public async Task InitializeAsync() =>
        _db = await _mongo.CreateDatabaseAsync($"igor-concurrency-{Guid.NewGuid():N}");

    public Task DisposeAsync() => Task.CompletedTask;

    // ─── Creation races ──────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureExistsAsync_ParallelCreators_ExactlyOneCreates()
    {
        Task<(GuildMember Member, bool Created)>[] tasks = Enumerable.Range(0, 12)
            .Select(_ => GuildMember.EnsureExistsAsync(
                _db, 1UL, 100UL, "user", "<@100>", MemberStatus.New, "join"))
            .ToArray();

        (GuildMember Member, bool Created)[] results = await Task.WhenAll(tasks);

        results.Count(r => r.Created).Should().Be(1);
        results.Should().OnlyContain(r => r.Member.ID == "1-100");
        (await _db.Find<GuildMember>().ManyAsync(m => m.Eq(f => f.MemberId, 100UL))).Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureExistsAsync_ExistingDocument_IsNotOverwritten()
    {
        (GuildMember first, _) = await GuildMember.EnsureExistsAsync(
            _db, 1UL, 101UL, "user", "<@101>", MemberStatus.New, "join");
        await first.TransitionToAsync(_db, MemberStatus.BannedByHoneypot, "honeypot");

        (GuildMember second, bool created) = await GuildMember.EnsureExistsAsync(
            _db, 1UL, 101UL, "user", "<@101>", MemberStatus.New, "join");

        created.Should().BeFalse();
        second.Status.Should().Be(MemberStatus.BannedByHoneypot);
        second.StatusHistory.Should().ContainSingle();
    }

    [Fact]
    public async Task GuildProperties_ParallelFirstUse_DoesNotResetCounter()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => GuildProperties.EnsureExistsAsync(_db, 7UL)));

        GuildProperties props = (await _db.Find<GuildProperties>().OneAsync("7"))!;
        props.ApplicationChannels.Should().Be(1UL);

        // Re-running after the counter advanced must not reset it.
        await _db.Update<GuildProperties>().MatchID("7").Modify(g => g.ApplicationChannels, 5UL).ExecuteAsync();
        await GuildProperties.EnsureExistsAsync(_db, 7UL);
        (await _db.Find<GuildProperties>().OneAsync("7"))!.ApplicationChannels.Should().Be(5UL);
    }

    [Fact]
    public async Task ReactivateAsync_FreshHoneypotBan_IsNotUndone()
    {
        (GuildMember member, _) = await GuildMember.EnsureExistsAsync(
            _db, 1UL, 102UL, "user", "<@102>", MemberStatus.New, "join");
        await member.TransitionToAsync(_db, MemberStatus.BannedByHoneypot, "honeypot");

        GuildMember stale = (await _db.Find<GuildMember>().OneAsync(member.ID))!;
        (await stale.ReactivateAsync(_db)).Should().BeFalse();

        (await _db.Find<GuildMember>().OneAsync(member.ID))!.Status.Should().Be(MemberStatus.BannedByHoneypot);
    }

    [Fact]
    public async Task ReactivateAsync_ReturningMember_ResetsAtomically()
    {
        (GuildMember member, _) = await GuildMember.EnsureExistsAsync(
            _db, 1UL, 103UL, "user", "<@103>", MemberStatus.New, "join");
        await member.TransitionToAsync(_db, MemberStatus.LeftVoluntarily, "left");

        GuildMember loaded = (await _db.Find<GuildMember>().OneAsync(member.ID))!;
        (await loaded.ReactivateAsync(_db)).Should().BeTrue();

        GuildMember after = (await _db.Find<GuildMember>().OneAsync(member.ID))!;
        after.Status.Should().Be(MemberStatus.New);
        after.LeftAt.Should().BeNull();
        after.StatusHistory.Select(e => e.To).Should().Equal(MemberStatus.LeftVoluntarily, MemberStatus.New);
    }

    // ─── Conditional transitions ─────────────────────────────────────────────

    [Fact]
    public async Task TryTransition_StrangerRoleRemoved_NeverDowngradesFullMember()
    {
        (GuildMember member, _) = await GuildMember.EnsureExistsAsync(
            _db, 1UL, 110UL, "user", "<@110>", MemberStatus.New, "join");
        GuildMember stale = (await _db.Find<GuildMember>().OneAsync(member.ID))!;

        await member.TransitionToAsync(_db, MemberStatus.FullMember);

        (await stale.TryTransitionAsync(_db, MemberStatus.StrangerRoleRemoved)).Should().BeFalse();
        stale.Status.Should().Be(MemberStatus.New, "a rejected transition must not touch the in-memory state");

        GuildMember loaded = (await _db.Find<GuildMember>().OneAsync(member.ID))!;
        loaded.Status.Should().Be(MemberStatus.FullMember);
        loaded.StatusHistory.Should().ContainSingle();
    }

    [Fact]
    public async Task TryTransition_ConcurrentKickAndBan_ExactlyOneWins()
    {
        (GuildMember member, _) = await GuildMember.EnsureExistsAsync(
            _db, 1UL, 111UL, "user", "<@111>", MemberStatus.Onboarding, "setup");
        GuildMember a = (await _db.Find<GuildMember>().OneAsync(member.ID))!;
        GuildMember b = (await _db.Find<GuildMember>().OneAsync(member.ID))!;

        // Both are conditional on the member still being present; after the first write the second sees a
        // terminal status and is rejected only if the winner is not in the allowed set.
        bool[] results = await Task.WhenAll(
            a.TryTransitionAsync(_db, MemberStatus.KickedByModerator, [MemberStatus.Onboarding]),
            b.TryTransitionAsync(_db, MemberStatus.BannedByModerator, [MemberStatus.Onboarding]));

        results.Count(r => r).Should().Be(1);
        GuildMember loaded = (await _db.Find<GuildMember>().OneAsync(member.ID))!;
        loaded.StatusHistory.Should().ContainSingle();
    }

    [Fact]
    public async Task TryTransition_ExtraMatch_RejectsWhenConditionNoLongerHolds()
    {
        (GuildMember member, _) = await GuildMember.EnsureExistsAsync(
            _db, 1UL, 112UL, "user", "<@112>", MemberStatus.Onboarding, "setup");
        StrangerApplicationEmbed app = new() { GuildId = 1UL, ChannelId = 2UL, MessageId = 3UL };
        await _db.SaveAsync(app);
        await _db.Update<GuildMember>().MatchID(member.ID).Modify(m => m.Application, app).ExecuteAsync();

        GuildMember snapshot = (await _db.Find<GuildMember>().OneAsync(member.ID))!;
        await snapshot.SetAutoKickEnabledAsync(_db, false);

        GuildMember stale = (await _db.Find<GuildMember>().OneAsync(member.ID))!;
        bool claimed = await stale.TryTransitionAsync(
            _db, MemberStatus.AutoKicked,
            extraMatch: f => f.Eq(m => m.Application!.IsAutoKickEnabled, true));

        claimed.Should().BeFalse();
        (await _db.Find<GuildMember>().OneAsync(member.ID))!.Status.Should().Be(MemberStatus.Onboarding);
    }

    // ─── Stale snapshots must not clobber ────────────────────────────────────

    [Fact]
    public async Task StaleSnapshot_TargetedWrites_DoNotRevertNewerStatusOrHistory()
    {
        (GuildMember member, _) = await GuildMember.EnsureExistsAsync(
            _db, 1UL, 120UL, "user", "<@120>", MemberStatus.Onboarding, "setup");
        GuildMember stale = (await _db.Find<GuildMember>().OneAsync(member.ID))!;

        await member.TransitionToAsync(_db, MemberStatus.FullMember, "promoted");

        await stale.StampLeftAtAsync(_db);
        await stale.UpdateDisplayAsync(_db, "renamed", "<@renamed>");
        await stale.SetOnboardingInProgressAsync(_db, true);

        GuildMember loaded = (await _db.Find<GuildMember>().OneAsync(member.ID))!;
        loaded.Status.Should().Be(MemberStatus.FullMember);
        loaded.StatusHistory.Should().ContainSingle();
        loaded.Member.Should().Be("renamed");
        loaded.IsOnboardingInProgress.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteChannel_OnlyClearsMatchingChannel()
    {
        (GuildMember member, _) = await GuildMember.EnsureExistsAsync(
            _db, 1UL, 121UL, "user", "<@121>", MemberStatus.Onboarding, "setup");

        NewbieChannel oldChannel = new() { GuildId = 1UL, ChannelId = 10UL, ChannelName = "old", Mention = "<#10>" };
        NewbieChannel newChannel = new() { GuildId = 1UL, ChannelId = 11UL, ChannelName = "new", Mention = "<#11>" };
        await _db.SaveAsync(oldChannel);
        await _db.SaveAsync(newChannel);

        GuildMember stale = (await _db.Find<GuildMember>().OneAsync(member.ID))!;
        await _db.Update<GuildMember>().MatchID(member.ID).Modify(m => m.Channel, oldChannel).ExecuteAsync();
        stale = (await _db.Find<GuildMember>().OneAsync(member.ID))!;

        // A newer channel replaces the old one while the stale snapshot still points at the old one.
        await _db.Update<GuildMember>().MatchID(member.ID).Modify(m => m.Channel, newChannel).ExecuteAsync();

        await stale.DeleteChannel(_db);

        (await _db.Find<GuildMember>().OneAsync(member.ID))!.Channel!.ChannelId.Should().Be(11UL);
    }

    [Fact]
    public async Task DeleteApplication_RemovesRecordAndReference()
    {
        (GuildMember member, _) = await GuildMember.EnsureExistsAsync(
            _db, 1UL, 122UL, "user", "<@122>", MemberStatus.Onboarding, "setup");
        StrangerApplicationEmbed app = new() { GuildId = 1UL, ChannelId = 2UL, MessageId = 30UL };
        await _db.SaveAsync(app);
        await _db.Update<GuildMember>().MatchID(member.ID).Modify(m => m.Application, app).ExecuteAsync();

        GuildMember loaded = (await _db.Find<GuildMember>().OneAsync(member.ID))!;
        await loaded.DeleteApplication(_db);

        (await _db.Find<GuildMember>().OneAsync(member.ID))!.Application.Should().BeNull();
        (await _db.Find<StrangerApplicationEmbed>().OneAsync(app.ID)).Should().BeNull();
    }

    // ─── Guild config versioning ─────────────────────────────────────────────

    [Fact]
    public async Task GuildConfig_ConcurrentEditors_OneWinsOtherGetsConflict()
    {
        GuildConfigService service = new(_db);
        await service.SaveAsync(new GuildConfig { GuildId = 55UL });

        GuildConfig a = (await service.GetAsync(55UL))!;
        GuildConfig b = (await service.GetAsync(55UL))!;
        a.IdleKickTimeSpan = TimeSpan.FromMinutes(5);
        b.AutoAssignStrangerRoleOnJoin = true;

        await service.SaveAsync(a);
        Func<Task> act = () => service.SaveAsync(b);

        await act.Should().ThrowAsync<ConfigConflictException>();
        GuildConfig loaded = (await service.GetAsync(55UL))!;
        loaded.IdleKickTimeSpan.Should().Be(TimeSpan.FromMinutes(5));
        loaded.AutoAssignStrangerRoleOnJoin.Should().BeFalse();
    }

    [Fact]
    public async Task GuildConfig_SameSnapshotCanBeSavedRepeatedly()
    {
        GuildConfigService service = new(_db);
        await service.SaveAsync(new GuildConfig { GuildId = 56UL });

        GuildConfig config = (await service.GetAsync(56UL))!;
        config.IdleKickTimeSpan = TimeSpan.FromMinutes(1);
        await service.SaveAsync(config);
        config.IdleKickTimeSpan = TimeSpan.FromMinutes(2);
        await service.SaveAsync(config);

        (await service.GetAsync(56UL))!.IdleKickTimeSpan.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task CachedGuildConfig_ReturnsIndependentCopies()
    {
        GuildConfigService inner = new(_db);
        await inner.SaveAsync(new GuildConfig { GuildId = 57UL, ApplicationModeratorRoleIds = [1UL] });
        CachedGuildConfigService cached = new(
            inner, Microsoft.Extensions.Logging.Abstractions.NullLogger<CachedGuildConfigService>.Instance);

        GuildConfig first = (await cached.GetAsync(57UL))!;
        first.ApplicationModeratorRoleIds.Add(2UL);
        first.Questionnaires["x"] = new Questionnaire { Id = "x", Name = "x" };

        GuildConfig second = (await cached.GetAsync(57UL))!;
        second.ApplicationModeratorRoleIds.Should().Equal(1UL);
        second.Questionnaires.Should().NotContainKey("x");
    }

    // ─── Background work tracking ────────────────────────────────────────────

    [Fact]
    public async Task BackgroundWork_DrainWaitsForInFlightWork()
    {
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool finished = false;

        ObservableBackgroundWork.Run(async _ =>
        {
            await release.Task;
            finished = true;
        }, CancellationToken.None, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, "test");

        Task<int> drain = ObservableBackgroundWork.DrainAsync(TimeSpan.FromSeconds(10));
        drain.IsCompleted.Should().BeFalse();

        release.SetResult();
        await drain;

        finished.Should().BeTrue();
    }
}
