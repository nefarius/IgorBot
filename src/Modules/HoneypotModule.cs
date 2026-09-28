using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

using IgorBot.Core;
using IgorBot.Services;

using JetBrains.Annotations;

using Nefarius.DSharpPlus.Extensions.Hosting.Events;

namespace IgorBot.Modules;

/// <summary>
///     Simple but effective anti-spambot feature that bans a server member if they post in a forbidden all-user writable
///     honeypot channel.
/// </summary>
[DiscordMessageCreatedEventSubscriber]
[UsedImplicitly]
internal sealed class HoneypotModule(
    IGuildConfigService guildConfigService,
    IHoneypotEnforcementService enforcement,
    ILogger<HoneypotModule> logger)
    : IDiscordMessageCreatedEventSubscriber
{
    public async Task DiscordOnMessageCreated(DiscordClient sender, MessageCreateEventArgs args)
    {
        try
        {
            if (args.Guild is null || args.Author.IsBot)
            {
                return;
            }

            GuildConfig? guildConfig = await guildConfigService.GetAsync(args.Guild.Id);
            if (guildConfig == null)
            {
                return;
            }

            if (!guildConfig.HoneypotChannelId.HasValue)
            {
                return;
            }

            if (args.Channel.Id != guildConfig.HoneypotChannelId.Value)
            {
                return;
            }

            DiscordMember member;
            try
            {
                member = await args.Guild.GetMemberAsync(args.Author.Id);
            }
            catch (NotFoundException)
            {
                logger.LogDebug("{Author} is not a member of {Guild}", args.Author, args.Guild);
                return;
            }

            HoneypotEligibility eligibility = HoneypotRules.Evaluate(
                member.IsBot,
                member.IsOwner,
                member.Roles.Select(r => r.Id),
                guildConfig.HoneypotExclusionRoleIds);

            if (eligibility == HoneypotEligibility.SkipOwner)
            {
                return;
            }

            if (eligibility == HoneypotEligibility.SkipExcludedRole)
            {
                logger.LogWarning("Member {Member} posted in honeypot channel but has excluded role", member);
                return;
            }

            if (eligibility != HoneypotEligibility.Enforce)
            {
                return;
            }

            await enforcement.EnforceAsync(args.Guild.Id, member.Id, member.ToString(), member.Mention);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Unexpected error in {nameof(DiscordOnMessageCreated)}");
        }
    }
}
