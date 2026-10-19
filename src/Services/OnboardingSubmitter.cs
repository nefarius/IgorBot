using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Schema;

using MongoDB.Entities;

namespace IgorBot.Services;

internal sealed class OnboardingSubmitter(
    DB db,
    IOnboardingQueue onboardingQueue,
    ILogger<OnboardingSubmitter> logger) : IOnboardingSubmitter
{
    public async Task SubmitAsync(DiscordGuild guild, GuildConfig guildConfig, GuildMember member,
        CancellationToken ct = default)
    {
        GuildProperties guildRuntime = await GuildProperties.EnsureExistsAsync(db, guild.Id);

        NewMemberMessage message = new()
        {
            GuildProperties = guildRuntime, GuildConfig = guildConfig, MemberEntryId = member.ID
        };

        logger.LogInformation("Submitting new member workflow message");

        await onboardingQueue.EnqueueAsync(message, ct);
    }
}
