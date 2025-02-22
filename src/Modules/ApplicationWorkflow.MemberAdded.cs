using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Util;

using MongoDB.Entities;

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

        if (!_config.CurrentValue.Guilds.ContainsKey(e.Guild.Id.ToString()))
        {
            return;
        }

        _logger.LogInformation("{Member} joined", e.Member);

        GuildMember guildMember = await DB.Find<GuildMember>().OneAsync(e.ToEntityId());

        if (guildMember is null)
        {
            guildMember = new GuildMember
            {
                GuildId = e.Guild.Id, 
                MemberId = e.Member.Id,
                Member = e.Member.ToString(),
                Mention = e.Member.Mention
            };

            await guildMember.SaveAsync();

            _logger.LogInformation("{Member} added to DB", e.Member);
        }

        guildMember.JoinedAt = DateTime.Now;
        guildMember.Reset();

        await guildMember.SaveAsync();

        _logger.LogInformation("{Member} updated in DB", e.Member);
        
        GuildConfig guildConfig = _config.CurrentValue.Guilds[e.Guild.Id.ToString()];
        DiscordRole strangerRole = e.Guild.GetRole(guildConfig.StrangerRoleId);

        if (e.Member.Roles.Contains(strangerRole))
        {
            _logger.LogInformation("{Member} has stranger role, submitting workflow", e.Member);
            
            await ProcessStrangerAssignment(e.Guild, guildConfig, guildMember);
        }
    }
}