using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

using IgorBot.Services;

namespace IgorBot.Modules;

internal sealed class IgorCoreModule(ILogger<IgorCoreModule> logger, IDiscordReadinessService readiness)
    : IEventHandler<GuildDownloadCompletedEventArgs>, IEventHandler<GuildAvailableEventArgs>
{
    public Task HandleEventAsync(DiscordClient sender, GuildDownloadCompletedEventArgs args)
    {
        foreach (DiscordGuild guild in args.Guilds.Values)
        {
            readiness.MarkGuildAvailable(guild.Id);
            logger.LogInformation("{Guild} downloaded", guild);
        }

        return Task.CompletedTask;
    }

    public Task HandleEventAsync(DiscordClient sender, GuildAvailableEventArgs args)
    {
        readiness.MarkGuildAvailable(args.Guild.Id);
        logger.LogInformation("{Guild} online", args.Guild);
        return Task.CompletedTask;
    }
}
