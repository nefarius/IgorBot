using System.Runtime.CompilerServices;

using DSharpPlus.Exceptions;

using FluentAssertions;

using IgorBot.Schema;
using IgorBot.Services;

using Microsoft.Extensions.Logging.Abstractions;

using MongoDB.Entities;

namespace IgorBot.Tests.Integration;

[Xunit.Collection("Mongo")]
public sealed class HoneypotEnforcementServiceTests : IAsyncLifetime
{
    private const ulong GuildId = 100UL;
    private const ulong MemberId = 200UL;

    private readonly MongoFixture _mongo;
    private DB _db = null!;
    private FakeHoneypotBanClient _banClient = null!;
    private HoneypotEnforcementService _sut = null!;

    public HoneypotEnforcementServiceTests(MongoFixture mongo) => _mongo = mongo;

    public async Task InitializeAsync()
    {
        _db = await _mongo.CreateDatabaseAsync($"igor-honeypot-enforce-{Guid.NewGuid():N}");
        _banClient = new FakeHoneypotBanClient();
        _sut = new HoneypotEnforcementService(
            _db,
            _banClient,
            NullLogger<HoneypotEnforcementService>.Instance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EnforceAsync_Success_MarksBannedByHoneypotAndBans()
    {
        HoneypotEnforcementOutcome outcome = await _sut.EnforceAsync(
            GuildId, MemberId, "user#0", "<@200>");

        outcome.Should().Be(HoneypotEnforcementOutcome.Banned);
        _banClient.Bans.Should().ContainSingle().Which.Should().Be((GuildId, MemberId));

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync($"{GuildId}-{MemberId}");
        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(MemberStatus.BannedByHoneypot);
        loaded.StatusReason.Should().Be("honeypot");
        loaded.BannedAt.Should().NotBeNull();
        loaded.RemovedByModeration.Should().BeTrue();
    }

    [Fact]
    public async Task EnforceAsync_BanFailsAndMemberStillPresent_RollsBackStatus()
    {
        _banClient.BanException = new InvalidOperationException("discord 503");
        _banClient.MemberPresent = true;

        HoneypotEnforcementOutcome outcome = await _sut.EnforceAsync(
            GuildId, MemberId, "user#0", "<@200>");

        outcome.Should().Be(HoneypotEnforcementOutcome.Failed);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync($"{GuildId}-{MemberId}");
        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(MemberStatus.New);
        loaded.StatusHistory.Select(e => e.Reason).Should()
            .Contain("honeypot").And.Contain("revert-honeypot-ban");
    }

    [Fact]
    public async Task EnforceAsync_BanFailsAndMemberGone_KeepsBannedByHoneypot()
    {
        _banClient.BanException = new InvalidOperationException("discord 503");
        _banClient.MemberPresent = false;

        HoneypotEnforcementOutcome outcome = await _sut.EnforceAsync(
            GuildId, MemberId, "user#0", "<@200>");

        outcome.Should().Be(HoneypotEnforcementOutcome.MemberGone);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync($"{GuildId}-{MemberId}");
        loaded!.Status.Should().Be(MemberStatus.BannedByHoneypot);
    }

    [Fact]
    public async Task EnforceAsync_BanFailsAndPresenceUnknown_RollsBackSoRetryCanBan()
    {
        _banClient.BanException = new InvalidOperationException("discord 503");
        _banClient.MemberPresent = null;

        HoneypotEnforcementOutcome outcome = await _sut.EnforceAsync(
            GuildId, MemberId, "user#0", "<@200>");

        outcome.Should().Be(HoneypotEnforcementOutcome.Failed);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync($"{GuildId}-{MemberId}");
        loaded!.Status.Should().Be(MemberStatus.New);
    }

    [Fact]
    public async Task EnforceAsync_FailedThenSuccess_RetriesAndBans()
    {
        _banClient.BanException = new InvalidOperationException("discord 503");
        _banClient.MemberPresent = true;
        await _sut.EnforceAsync(GuildId, MemberId, "user#0", "<@200>");

        _banClient.BanException = null;
        HoneypotEnforcementOutcome outcome = await _sut.EnforceAsync(
            GuildId, MemberId, "user#0", "<@200>");

        outcome.Should().Be(HoneypotEnforcementOutcome.Banned);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync($"{GuildId}-{MemberId}");
        loaded!.Status.Should().Be(MemberStatus.BannedByHoneypot);
        _banClient.Bans.Should().ContainSingle();
    }

    [Fact]
    public async Task EnforceAsync_PermanentForbidden_DoesNotRevertAndReturnsPermanentFailure()
    {
        _banClient.BanException = Uninitialized<UnauthorizedException>();

        HoneypotEnforcementOutcome outcome = await _sut.EnforceAsync(
            GuildId, MemberId, "user#0", "<@200>");

        outcome.Should().Be(HoneypotEnforcementOutcome.PermanentFailure);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync($"{GuildId}-{MemberId}");
        loaded!.Status.Should().Be(MemberStatus.BannedByHoneypot);
        loaded.StatusHistory.Select(e => e.Reason).Should().Contain("honeypot")
            .And.NotContain("revert-honeypot-ban");
    }

    [Fact]
    public async Task EnforceAsync_PermanentUnknownUser_DoesNotRevertAndReturnsPermanentFailure()
    {
        _banClient.BanException = Uninitialized<NotFoundException>();

        HoneypotEnforcementOutcome outcome = await _sut.EnforceAsync(
            GuildId, MemberId, "user#0", "<@200>");

        outcome.Should().Be(HoneypotEnforcementOutcome.PermanentFailure);
        (await _db.Find<GuildMember>().OneAsync($"{GuildId}-{MemberId}"))!.Status
            .Should().Be(MemberStatus.BannedByHoneypot);
    }

    [Fact]
    public async Task EnforceAsync_AlreadyBannedByHoneypot_PermanentFailure_DoesNotAddHistory()
    {
        GuildMember member = await InsertMember(MemberStatus.BannedByHoneypot);
        int historyBefore = member.StatusHistory.Count;
        _banClient.BanException = Uninitialized<UnauthorizedException>();

        HoneypotEnforcementOutcome outcome = await _sut.EnforceAsync(
            GuildId, MemberId, "user#0", "<@200>");

        outcome.Should().Be(HoneypotEnforcementOutcome.PermanentFailure);
        (await _db.Find<GuildMember>().OneAsync(member.ID))!.StatusHistory.Should().HaveCount(historyBefore);
    }

    [Fact]
    public async Task EnforceAsync_AlreadyBannedByHoneypot_RetriesDiscordBanWithoutNewTransition()
    {
        GuildMember member = await InsertMember(MemberStatus.BannedByHoneypot);
        await member.TransitionToAsync(_db, MemberStatus.BannedByHoneypot, "honeypot");

        GuildMember existing = (await _db.Find<GuildMember>().OneAsync(member.ID))!;
        int historyBefore = existing.StatusHistory.Count;

        HoneypotEnforcementOutcome outcome = await _sut.EnforceAsync(
            GuildId, MemberId, "user#0", "<@200>");

        outcome.Should().Be(HoneypotEnforcementOutcome.AlreadyBanned);
        _banClient.BanCallCount.Should().Be(1);

        GuildMember? loaded = await _db.Find<GuildMember>().OneAsync(member.ID);
        loaded!.Status.Should().Be(MemberStatus.BannedByHoneypot);
        loaded.StatusHistory.Should().HaveCount(historyBefore);
    }

    [Fact]
    public async Task EnforceAsync_AlreadyBannedByModerator_SkipsDiscordBan()
    {
        GuildMember member = await InsertMember(MemberStatus.New);
        await member.TransitionToAsync(_db, MemberStatus.BannedByModerator, "mod panel");

        HoneypotEnforcementOutcome outcome = await _sut.EnforceAsync(
            GuildId, MemberId, "user#0", "<@200>");

        outcome.Should().Be(HoneypotEnforcementOutcome.AlreadyBanned);
        _banClient.BanCallCount.Should().Be(0);
        (await _db.Find<GuildMember>().OneAsync(member.ID))!.Status
            .Should().Be(MemberStatus.BannedByModerator);
    }

    [Fact]
    public async Task EnforceAsync_IdempotentAfterSuccessfulBan()
    {
        await _sut.EnforceAsync(GuildId, MemberId, "user#0", "<@200>");
        HoneypotEnforcementOutcome again = await _sut.EnforceAsync(
            GuildId, MemberId, "user#0", "<@200>");

        again.Should().Be(HoneypotEnforcementOutcome.AlreadyBanned);
        _banClient.BanCallCount.Should().Be(2);
        (await _db.Find<GuildMember>().OneAsync($"{GuildId}-{MemberId}"))!.StatusHistory
            .Should().ContainSingle(e => e.To == MemberStatus.BannedByHoneypot);
    }

    private static Exception Uninitialized<T>() where T : Exception =>
        (Exception)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private async Task<GuildMember> InsertMember(MemberStatus status)
    {
        GuildMember member = new()
        {
            GuildId = GuildId,
            MemberId = MemberId,
            Member = "user#0",
            Mention = "<@200>",
            ID = $"{GuildId}-{MemberId}",
            Status = status
        };
        await _db.SaveAsync(member);
        return member;
    }
}
