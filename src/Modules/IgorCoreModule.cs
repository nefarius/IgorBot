using DSharpPlus;
using DSharpPlus.EventArgs;

using JetBrains.Annotations;

using Nefarius.DSharpPlus.Extensions.Hosting.Events;

namespace IgorBot.Modules;

[DiscordGuildAvailableEventSubscriber]
[UsedImplicitly]
internal class IgorCoreModule(ILogger<IgorCoreModule> logger) : IDiscordGuildAvailableEventSubscriber
{
    public Task DiscordOnGuildAvailable(DiscordClient sender, GuildCreateEventArgs args)
    {
        logger.LogInformation("{Guild} online", args.Guild);

        return Task.CompletedTask;
    }
}