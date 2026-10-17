using DSharpPlus;
using DSharpPlus.Entities.AuditLogs;
using DSharpPlus.EventArgs;

using IgorBot.Schema;
using IgorBot.Services;
using IgorBot.Util;

using MongoDB.Entities;

namespace IgorBot.Modules;

/// <summary>
///     Handles native guild audit-log entries. When a kick is detected, the member is
///     pre-marked as <see cref="MemberStatus.KickedExternally" /> before the subsequent
///     member-removed event can classify the departure as voluntary.
/// </summary>
internal sealed class AuditLogKickHandler(
    DB db,
    ILogger<AuditLogKickHandler> logger,
    IGuildConfigService guildConfigService)
    : IEventHandler<GuildAuditLogCreatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildAuditLogCreatedEventArgs args)
    {
        if (args.AuditLogEntry is not DiscordAuditLogKickEntry kick || kick.Target is null)
        {
            return;
        }

        ulong guildId = args.Guild.Id;
        ulong targetId = kick.Target.Id;
        ulong? actorId = kick.UserResponsible?.Id;
        string? reason = kick.Reason;

        if (await guildConfigService.GetAsync(guildId) is null)
        {
            return;
        }

        string entityId = DiscordEntityIds.ForMember(guildId, targetId);
        GuildMember? member = await db.Find<GuildMember>().OneAsync(entityId);

        if (member is null)
        {
            logger.LogDebug(
                "Audit-log kick: member {TargetId} in guild {GuildId} not found in DB, skipping",
                targetId, guildId);
            return;
        }

        if (!ExternalKickPolicy.ShouldPreMark(member.Status))
        {
            logger.LogInformation(
                "Audit-log kick for {TargetId}: status already {Status}, skipping pre-mark",
                targetId, member.Status);
            return;
        }

        logger.LogInformation(
            "External kick detected via audit log: member {TargetId} in guild {GuildId} kicked by actor {ActorId} (reason={Reason}). Pre-marking as KickedExternally.",
            targetId, guildId, actorId, reason);

        await member.TransitionToAsync(db, MemberStatus.KickedExternally,
            reason: reason ?? "external kick via audit log",
            actorId: actorId);
    }
}
