using FluentAssertions;

using IgorBot.Core;
using IgorBot.Services;

using MongoDB.Entities;

namespace IgorBot.Tests.Integration;

[Xunit.Collection("Mongo")]
public sealed class GuildConfigHoneypotActivationTests : IAsyncLifetime
{
    private const ulong GuildId = 42UL;

    private readonly MongoFixture _mongo;
    private DB _db = null!;
    private GuildConfigService _sut = null!;

    public GuildConfigHoneypotActivationTests(MongoFixture mongo) => _mongo = mongo;

    public async Task InitializeAsync()
    {
        _db = await _mongo.CreateDatabaseAsync($"igor-honeypot-activation-{Guid.NewGuid():N}");
        _sut = new GuildConfigService(_db);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SaveAsync_NewHoneypotChannel_StampsActivation()
    {
        DateTime before = DateTime.UtcNow;
        DateTime lowerBound = before.AddMilliseconds(-1);
        GuildConfig config = MinimalConfig(channelId: 10);

        await _sut.SaveAsync(config);

        GuildConfig? loaded = await _sut.GetAsync(GuildId);
        loaded!.HoneypotChannelId.Should().Be(10UL);
        loaded.HoneypotChannelActivatedAt.Should().NotBeNull();
        loaded.HoneypotChannelActivatedAt!.Value.Should().BeOnOrAfter(lowerBound);
        config.HoneypotChannelActivatedAt.Should()
            .BeCloseTo(loaded.HoneypotChannelActivatedAt.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task SaveAsync_UnrelatedUpdate_PreservesActivation()
    {
        await _sut.SaveAsync(MinimalConfig(channelId: 10));
        DateTime? stamped = (await _sut.GetAsync(GuildId))!.HoneypotChannelActivatedAt;
        stamped.Should().NotBeNull();

        await Task.Delay(20);

        GuildConfig update = (await _sut.GetAsync(GuildId))!;
        update.IdleKickTimeSpan = TimeSpan.FromMinutes(15);
        await _sut.SaveAsync(update);

        GuildConfig? loaded = await _sut.GetAsync(GuildId);
        loaded!.HoneypotChannelId.Should().Be(10UL);
        loaded.IdleKickTimeSpan.Should().Be(TimeSpan.FromMinutes(15));
        loaded.HoneypotChannelActivatedAt.Should().Be(stamped);
    }

    [Fact]
    public async Task SaveAsync_ChannelChange_RestampsActivation()
    {
        await _sut.SaveAsync(MinimalConfig(channelId: 10));
        DateTime? first = (await _sut.GetAsync(GuildId))!.HoneypotChannelActivatedAt;

        await Task.Delay(20);

        GuildConfig update = (await _sut.GetAsync(GuildId))!;
        update.HoneypotChannelId = 20;
        await _sut.SaveAsync(update);

        GuildConfig? loaded = await _sut.GetAsync(GuildId);
        loaded!.HoneypotChannelId.Should().Be(20UL);
        loaded.HoneypotChannelActivatedAt.Should().NotBeNull();
        loaded.HoneypotChannelActivatedAt.Should().BeAfter(first!.Value);
    }

    [Fact]
    public async Task SaveAsync_StaleSnapshot_DoesNotOverwriteNewerHoneypotChannel()
    {
        await _sut.SaveAsync(MinimalConfig(channelId: 10));
        GuildConfig stale = (await _sut.GetAsync(GuildId))!;

        GuildConfig newer = (await _sut.GetAsync(GuildId))!;
        newer.HoneypotChannelId = 20;
        await _sut.SaveAsync(newer);
        DateTime? activationAfterChannelChange = (await _sut.GetAsync(GuildId))!.HoneypotChannelActivatedAt;

        stale.IdleKickTimeSpan = TimeSpan.FromMinutes(15);
        await _sut.SaveAsync(stale);

        GuildConfig? loaded = await _sut.GetAsync(GuildId);
        loaded!.HoneypotChannelId.Should().Be(20UL);
        loaded.IdleKickTimeSpan.Should().Be(TimeSpan.FromMinutes(15));
        loaded.HoneypotChannelActivatedAt.Should().Be(activationAfterChannelChange);
    }

    [Fact]
    public async Task SaveAsync_ClearingChannel_ClearsActivation()
    {
        await _sut.SaveAsync(MinimalConfig(channelId: 10));

        GuildConfig update = (await _sut.GetAsync(GuildId))!;
        update.HoneypotChannelId = null;
        await _sut.SaveAsync(update);

        GuildConfig? loaded = await _sut.GetAsync(GuildId);
        loaded!.HoneypotChannelId.Should().BeNull();
        loaded.HoneypotChannelActivatedAt.Should().BeNull();
    }

    private static GuildConfig MinimalConfig(ulong? channelId) =>
        new()
        {
            GuildId = GuildId,
            HoneypotChannelId = channelId
        };
}
