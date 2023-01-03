using System.Diagnostics.CodeAnalysis;

using Coravel.Invocable;

using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Schema;

using Microsoft.Extensions.Options;

using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting;

namespace IgorBot.Invocables;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal class KickStaleInvokable : IInvocable
{
    private readonly IOptions<IgorConfig> _config;
    private readonly IDiscordClientService _discord;
    private readonly ILogger<KickStaleInvokable> _logger;

    public KickStaleInvokable(
        ILogger<KickStaleInvokable> logger,
        IOptions<IgorConfig> config,
        IDiscordClientService discord
    )
    {
        _logger = logger;
        _config = config;
        _discord = discord;
    }

    public async Task Invoke()
    {
        _logger.LogDebug("Running stale kick timer");

        // Enumerate guild configs with an active idle timespan set
        foreach (GuildConfig config in _config.Value.Guilds
                     .Where(gc => gc.Value.IdleKickTimeSpan.HasValue)
                     .Select(gc => gc.Value))
        {
            // query for members of the current guild where the application lifetime has exceeded the allowed idle time
            List<GuildMember> staleMembers = await DB.Find<GuildMember>()
                .ManyAsync(m =>
                    // match the Guild we're enumerating
                    m.Eq(f => f.GuildId, config.GuildId) &
                    // don't kick users with a pending submission
                    m.Eq(f => f.Application.QuestionnaireSubmittedAt, null) &
                    // auto-kick might be disabled
                    m.Eq(f => f.Application.IsAutoKickEnabled, true) &
                    // get embeds that are older than the configured timespan
                    m.Lt(f => f.Application.CreatedAt, DateTime.UtcNow.Add(-config.IdleKickTimeSpan.Value))
                );

            DiscordGuild guild = _discord.Client.Guilds[config.GuildId];

            _logger.LogDebug("Running stale members check for {Guild}", guild);

            if (!staleMembers.Any())
            {
                _logger.LogDebug("No stale members found");
            }

            foreach (GuildMember guildMember in staleMembers)
            {
                _logger.LogInformation("Processing stale member {MemberId}", guildMember.MemberId);

                try
                {
                    DiscordMember member = await guild.GetMemberAsync(guildMember.MemberId);

                    await member.RemoveAsync("Member removed due to idle timeout");

                    _logger.LogInformation("Removed {Member} due to idle timeout", member);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to remove guild member");
                }
            }
        }
    }
}
