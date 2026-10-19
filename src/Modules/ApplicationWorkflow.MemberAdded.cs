using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Util;

namespace IgorBot.Modules;

internal partial class ApplicationWorkflow
{
    //
    // Called when member joined the Guild
    // 
    public async Task HandleEventAsync(DiscordClient sender, GuildMemberAddedEventArgs e)
    {
        if (e.Member.IsBot)
        {
            return;
        }

        GuildConfig? guildConfig = await guildConfigService.GetAsync(e.Guild.Id);
        if (guildConfig == null)
        {
            return;
        }

        logger.LogInformation("{Member} joined", e.Member);

        // Atomic create-or-load: concurrent creators (honeypot, sync, reconciliation) never collide.
        (GuildMember member, bool isFirstJoin) = await GuildMember.EnsureExistsAsync(
            db, e.Guild.Id, e.Member.Id, e.Member.ToString(), e.Member.Mention,
            MemberStatus.New, "join");

        if (isFirstJoin)
        {
            logger.LogInformation("{Member} added to DB, initialized as New (first join)", e.Member);
        }
        else if (!await member.ReactivateAsync(db))
        {
            logger.LogWarning("{Member} rejoin skipped: a honeypot ban was just recorded", e.Member);
            return;
        }

        logger.LogInformation("{Member} updated in DB", e.Member);
        if (!e.Guild.Roles.TryGetValue(guildConfig.StrangerRoleId, out DiscordRole? strangerRole))
        {
            return;
        }

        if (guildConfig.AutoAssignStrangerRoleOnJoin && !e.Member.Roles.Contains(strangerRole))
        {
            logger.LogInformation("{Member} auto-assigning stranger role", e.Member);
            await e.Member.GrantRoleAsync(strangerRole);
            return;
        }

        if (e.Member.Roles.Contains(strangerRole) && guildConfig.EnableOnboardingWorkflow)
        {
            logger.LogInformation("{Member} has stranger role, submitting workflow", e.Member);

            await ProcessStrangerAssignment(e.Guild, guildConfig, member);
        }
    }
}