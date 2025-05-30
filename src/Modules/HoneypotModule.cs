using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

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
            if (args.Author.IsBot)
            {
                return;
            }

            DiscordMember member = await args.Guild.GetMemberAsync(args.Author.Id);

            // do not apply to privileged accounts
            if (member.IsOwner)
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

            // member is on the exclusion list and protected
            if (member.Roles.Any(r => guildConfig.HoneypotExclusionRoleIds.Contains(r.Id)))
            {
                logger.LogWarning("Member {Member} posted in honeypot channel but has excluded role", member);
                return;
            }

            // yeet!
            logger.LogInformation("Banning {Member} due to messaging in honeypot channel", member);
            await member.BanAsync(1, "User fell into honeypot trap");
            logger.LogInformation("{Member} banned", member);
        }
        catch (NotFoundException)
        {
            logger.LogDebug("{Author} is not a member of {Guild}", args.Author, args.Guild);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Unexpected error in {nameof(DiscordOnMessageCreated)}");
        }
    }
}