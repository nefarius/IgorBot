using DSharpPlus;
using DSharpPlus.EventArgs;

using IgorBot.Schema;
using IgorBot.Util;

namespace IgorBot.Modules;

internal partial class ApplicationWorkflow
{
    //
    // Called when ANY ban is issued in the guild (Discord UI, another bot, or this bot).
    // We use this to catch external bans that go through GuildMemberRemoved without a
    // more-specific cause already set.
    //
    public async Task HandleEventAsync(DiscordClient sender, GuildBanAddedEventArgs e)
    {
        if (e.Member.IsBot)
        {
            return;
        }

        if (await guildConfigService.GetAsync(e.Guild.Id) == null)
        {
            return;
        }

        GuildMember? member = await db.Find<GuildMember>().OneAsync(e.Member.ToEntityId());

        if (member is null)
        {
            return;
        }

        // If the ban was already recorded by the bot (panel action or honeypot) the
        // Status will already be a terminal ban state — don't overwrite it.
        if (member.Status is MemberStatus.BannedByModerator
            or MemberStatus.BannedByHoneypot
            or MemberStatus.BannedExternally)
        {
            logger.LogInformation(
                "GuildBanAdded for {Member} skipped: status already {Status}",
                e.Member, member.Status);
            return;
        }

        logger.LogWarning("{Member} was banned externally (outside the bot)", e.Member);

        // Conditional: skip when a bot-driven ban status landed between the read above and this write.
        await member.TryTransitionAsync(
            db,
            MemberStatus.BannedExternally,
            [
                MemberStatus.Unknown, MemberStatus.New, MemberStatus.Onboarding,
                MemberStatus.QuestionnaireSubmitted, MemberStatus.FullMember, MemberStatus.StrangerRoleRemoved,
                MemberStatus.LeftVoluntarily, MemberStatus.KickedByModerator, MemberStatus.KickedExternally,
                MemberStatus.AutoKicked
            ],
            "external ban");
    }
}
