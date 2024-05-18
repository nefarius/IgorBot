using System.Diagnostics.CodeAnalysis;

using Coravel.Invocable;

using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Schema;

using Microsoft.Extensions.Options;

using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting;

namespace IgorBot.Invocables;

/// <summary>
///     Scheduled task to query for stale users and kick them.
/// </summary>
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal class KickStaleInvokable : IInvocable
{
    private readonly IOptionsMonitor<IgorConfig> _config;
    private readonly IDiscordClientService _discord;
    private readonly ILogger<KickStaleInvokable> _logger;

    public KickStaleInvokable(
        ILogger<KickStaleInvokable> logger,
        IOptionsMonitor<IgorConfig> config,
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
        foreach (GuildConfig config in _config.CurrentValue.Guilds
                     .Where(gc => gc.Value.IdleKickTimeSpan.HasValue)
                     .Select(gc => gc.Value))
        {
            // query for members of the current guild where the application lifetime has exceeded the allowed idle time
            List<GuildMember> staleMembers = await DB.Find<GuildMember>()
                .ManyAsync(m =>
                    m.Lt(f => f.Application.CreatedAt, DateTime.UtcNow.Add(-config.IdleKickTimeSpan!.Value)) &
                    m.Eq(f => f.GuildId, config.GuildId) &
                    m.Eq(f => f.Application.IsAutoKickEnabled, true) &
                    m.Eq(f => f.Application.QuestionnaireSubmittedAt, null) &
                    m.Eq(f => f.PromotedAt, null) &
                    m.Eq(f => f.StrangerRoleRemovedAt, null) & 
                    m.Eq(f => f.FullMemberAt, null) &
                    m.Eq(f => f.AutoKickedAt, null)
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
                    guildMember.AutoKickedAt = DateTime.UtcNow;
                    await guildMember.SaveAsync();

                    try
                    {
                        DiscordMember member = await guild.GetMemberAsync(guildMember.MemberId);

                        await member.RemoveAsync("Member removed due to idle timeout");

                        _logger.LogWarning("Removed {@Member} due to idle timeout", guildMember);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to auto-remove {@Member}", guildMember);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to remove guild member");
                }
            }
        }
    }
}
