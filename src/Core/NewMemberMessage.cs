using IgorBot.Schema;

namespace IgorBot.Core;

/// <summary>
///     Message payload for the new member onboarding workflow queue.
/// </summary>
internal sealed class NewMemberMessage
{
    public GuildProperties GuildProperties { get; init; }

    public GuildConfig GuildConfig { get; init; }

    public string MemberEntryId { get; init; }
}