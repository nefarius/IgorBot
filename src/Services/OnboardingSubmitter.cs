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
        GuildProperties? guildRuntime = await db.Find<GuildProperties>().OneAsync(guild.Id.ToString());

        if (guildRuntime is null)
        {
            guildRuntime = new GuildProperties { GuildId = guild.Id };
            await db.SaveAsync(guildRuntime);
        }

        NewMemberMessage message = new()
        {
            GuildProperties = guildRuntime, GuildConfig = guildConfig, MemberEntryId = member.ID
        };

        logger.LogInformation("Submitting new member workflow message");

        await onboardingQueue.EnqueueAsync(message, ct);
    }
}
