using DSharpPlus;
using DSharpPlus.EventArgs;

using IgorBot.Services;

using JetBrains.Annotations;

using Nefarius.DSharpPlus.Extensions.Hosting.Events;

namespace IgorBot.Modules;

[DiscordGuildAvailableEventSubscriber]
[UsedImplicitly]
internal class IgorCoreModule(ILogger<IgorCoreModule> logger, IDiscordReadinessService readiness)
    : IDiscordGuildAvailableEventSubscriber
{
    public Task DiscordOnGuildAvailable(DiscordClient sender, GuildCreateEventArgs args)
    {
        readiness.MarkGuildAvailable(args.Guild.Id);

        logger.LogInformation("{Guild} online", args.Guild);

        return Task.CompletedTask;
    }
}