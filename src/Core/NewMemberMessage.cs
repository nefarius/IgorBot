using IgorBot.Schema;

namespace IgorBot.Core;

/// <summary>
///     Message payload for the new member onboarding workflow queue.
/// </summary>
internal sealed class NewMemberMessage
{
    public required GuildProperties GuildProperties { get; init; }

    public required GuildConfig GuildConfig { get; init; }

    public required string MemberEntryId { get; init; }
}