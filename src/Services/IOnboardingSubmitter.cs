using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Schema;

namespace IgorBot.Services;

/// <summary>
///     Submits a member to the onboarding queue (channel + status widget creation).
/// </summary>
internal interface IOnboardingSubmitter
{
    Task SubmitAsync(DiscordGuild guild, GuildConfig guildConfig, GuildMember member, CancellationToken ct = default);
}
