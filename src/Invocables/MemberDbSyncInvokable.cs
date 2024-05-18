using Coravel.Invocable;

using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Util;

using Microsoft.Extensions.Options;

using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting;

namespace IgorBot.Invocables;

internal class MemberDbSyncInvokable : IInvocable
{
    private readonly IOptionsMonitor<IgorConfig> _config;
    private readonly IDiscordClientService _discord;
    private readonly ILogger<MemberDbSyncInvokable> _logger;

    public MemberDbSyncInvokable(IOptionsMonitor<IgorConfig> config, IDiscordClientService discord,
        ILogger<MemberDbSyncInvokable> logger)
    {
        _config = config;
        _discord = discord;
        _logger = logger;
    }

    public async Task Invoke()
    {
        _logger.LogInformation("Running members database synchronization");

        foreach (GuildConfig config in _config.CurrentValue.Guilds.Select(gc => gc.Value))
        {
            DiscordGuild guild = _discord.Client.Guilds[config.GuildId];

            _logger.LogDebug("Processing members of {Guild}", guild);

            await foreach (DiscordMember member in guild.GetAllMembersAsync())
            {
                if (member.IsBot)
                {
                    continue;
                }

                string id = member.ToEntityId();
                GuildMember guildMember = await DB.Find<GuildMember>().OneAsync(id);

                if (guildMember is not null)
                {
                    continue;
                }

                guildMember = new GuildMember
                {
                    GuildId = member.Guild.Id,
                    MemberId = member.Id,
                    Member = member.ToString(),
                    Mention = member.Mention
                };
                
                await guildMember.SaveAsync();

                _logger.LogInformation("{Member} added to DB", member);
            }
        }

        _logger.LogInformation("Members database synchronization done");
    }
}