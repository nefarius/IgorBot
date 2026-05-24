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
    public async Task DiscordOnGuildMemberAdded(DiscordClient sender, GuildMemberAddEventArgs e)
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

        GuildMember? guildMember = await db.Find<GuildMember>().OneAsync(e.ToEntityId());

        bool isFirstJoin = guildMember is null;

        if (isFirstJoin)
        {
            guildMember = new GuildMember
            {
                GuildId = e.Guild.Id,
                MemberId = e.Member.Id,
                Member = e.Member.ToString(),
                Mention = e.Member.Mention
            };

            await db.SaveAsync(guildMember);

            logger.LogInformation("{Member} added to DB", e.Member);
        }

        // guildMember is guaranteed non-null: either it existed before or was just created above.
        GuildMember member = guildMember!;
        member.JoinedAt = DateTime.UtcNow;

        if (isFirstJoin)
        {
            // Brand-new member: initialize canonical state without recording a misleading "rejoin" history entry.
            DateTime now = DateTime.UtcNow;
            member.Status = MemberStatus.New;
            member.StatusChangedAt = now;
            member.StatusReason = "join";
            logger.LogInformation("GuildMember {MemberId} initialized as New (first join)", member.ID);
        }
        else
        {
            // Returning member: reset lifecycle fields and record a rejoin history event.
            member.Reset();
        }

        await db.SaveAsync(member);

        logger.LogInformation("{Member} updated in DB", e.Member);
        DiscordRole strangerRole = e.Guild.GetRole(guildConfig.StrangerRoleId);

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