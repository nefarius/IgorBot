using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Nefarius.DSharpPlus.Extensions.Hosting.Events;

namespace IgorBot.Modules
{
    [DiscordGuildAvailableEventSubscriber]
    internal class IgorCoreModule : IDiscordGuildAvailableEventSubscriber
    {
        private readonly ILogger<IgorCoreModule> _logger;

        public IgorCoreModule(ILogger<IgorCoreModule> logger)
        {
            _logger = logger;
        }

        public async Task DiscordOnGuildAvailable(DiscordClient sender, GuildCreateEventArgs args)
        {
            _logger.LogInformation("{Guild} online", args.Guild);
        }
    }
}
