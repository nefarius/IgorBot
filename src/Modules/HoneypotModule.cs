using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

using IgorBot.Core;

using JetBrains.Annotations;

using Microsoft.Extensions.Options;

using Nefarius.DSharpPlus.Extensions.Hosting.Events;

namespace IgorBot.Modules;

/// <summary>
///     Simple but effective anti-spambot feature that bans a server member if they post in a forbidden all-user writable
///     honeypot channel.
/// </summary>
[DiscordMessageCreatedEventSubscriber]
[UsedImplicitly]
internal sealed class HoneypotModule(IOptionsMonitor<IgorConfig> config, ILogger<HoneypotModule> logger)
    : IDiscordMessageCreatedEventSubscriber
{
    public async Task DiscordOnMessageCreated(DiscordClient sender, MessageCreateEventArgs args)
    {
        try
        {
            DiscordMember member = (DiscordMember)args.Author;

            // do not apply to privileged accounts
            if (args.Author.IsBot || member.IsOwner)
            {
                return;
            }

            // config missing for this guild
            if (!config.CurrentValue.Guilds.ContainsKey(args.Guild.Id.ToString()))
            {
                return;
            }

            GuildConfig guildConfig = config.CurrentValue.Guilds[args.Guild.Id.ToString()];

            // feature not configured
            if (!guildConfig.HoneypotChannelId.HasValue)
            {
                return;
            }

            // not the channel of interest
            if (args.Channel.Id != guildConfig.HoneypotChannelId.Value)
            {
                return;
            }

            // yeet!
            logger.LogInformation("Banning {Member} due to messaging in honeypot channel", member);
            await member.BanAsync(1, "User fell into honeypot trap");
            logger.LogInformation("{Member} banned", member);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Unexpected error in {nameof(DiscordOnMessageCreated)}");
        }
    }
}