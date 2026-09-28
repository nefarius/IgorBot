using IgorBot.Services;

namespace IgorBot.Tests.Integration;

/// <summary>
///     In-memory <see cref="IHoneypotBanClient" /> for enforcement tests.
/// </summary>
internal sealed class FakeHoneypotBanClient : IHoneypotBanClient
{
    public List<(ulong GuildId, ulong UserId)> Bans { get; } = [];

    public Exception? BanException { get; set; }

    public bool? MemberPresent { get; set; } = true;

    public int BanCallCount { get; private set; }

    public Task BanAsync(ulong guildId, ulong userId)
    {
        BanCallCount++;
        if (BanException is not null)
        {
            throw BanException;
        }

        Bans.Add((guildId, userId));
        return Task.CompletedTask;
    }

    public Task<bool?> IsMemberPresentAsync(ulong guildId, ulong userId) =>
        Task.FromResult(MemberPresent);
}
