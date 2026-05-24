using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;
using IgorBot.Util;

using JetBrains.Annotations;

using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting.Events;

namespace IgorBot.Modules;

/// <summary>
///     Simple but effective anti-spambot feature that bans a server member if they post in a forbidden all-user writable
///     honeypot channel.
/// </summary>
[DiscordMessageCreatedEventSubscriber]
[UsedImplicitly]
internal sealed class HoneypotModule(DB db, IGuildConfigService guildConfigService, ILogger<HoneypotModule> logger)
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

            GuildConfig guildConfig = await guildConfigService.GetAsync(args.Guild.Id);
            if (guildConfig == null)
            {
                return;
            }

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

            // Mark the ban in our DB before the Discord call so the subsequent
            // GuildMemberRemoved event sees it as a moderation removal, not a self-leave.
            GuildMember guildMember = await db.Find<GuildMember>().OneAsync(member.ToEntityId());

            bool isNewDocument = guildMember is null;
            if (isNewDocument)
            {
                guildMember = new GuildMember
                {
                    GuildId = args.Guild.Id,
                    MemberId = member.Id,
                    Member = member.ToString(),
                    Mention = member.Mention
                };
                await db.SaveAsync(guildMember);
            }

            MemberStatus previousStatus = guildMember.Status;

            logger.LogInformation(
                "Honeypot triggered by {Member} (existing document: {Existing}, prior status {Previous})",
                member, !isNewDocument, previousStatus);

            await guildMember.TransitionToAsync(db, MemberStatus.BannedByHoneypot, "honeypot");

            // yeet!
            logger.LogInformation("Banning {Member} due to messaging in honeypot channel", member);
            try
            {
                await member.BanAsync(1, "User fell into honeypot trap");
            }
            catch (Exception banEx)
            {
                logger.LogError(banEx, "BanAsync failed for honeypot member {Member}, reverting DB state", member);
                try
                {
                    // Newly-created documents have no meaningful prior history — revert to New.
                    // Existing documents are rolled back to whatever state they were in before.
                    MemberStatus revertTo = isNewDocument ? MemberStatus.New : previousStatus;
                    await guildMember.TransitionToAsync(db, revertTo, "revert-honeypot-ban");
                }
                catch (Exception revertEx)
                {
                    logger.LogError(revertEx,
                        "Failed to revert DB state for {Member} after BanAsync failure", member);
                }

                throw;
            }

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