using System.Text.Json;

using DSharpPlus;
using DSharpPlus.EventArgs;

using IgorBot.Schema;
using IgorBot.Services;

using JetBrains.Annotations;

using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting.Events;

namespace IgorBot.Modules;

/// <summary>
///     Handles <c>GUILD_AUDIT_LOG_ENTRY_CREATE</c> gateway events, which DSharpPlus 4.5.1
///     does not natively parse. When a kick (action_type 20) is detected, the member is
///     pre-marked as <see cref="MemberStatus.KickedExternally" /> in the database before the
///     subsequent <c>GUILD_MEMBER_REMOVE</c> event fires. This prevents
///     <see cref="ApplicationWorkflow.DiscordOnGuildMemberRemoved" /> from misclassifying
///     the departure as a voluntary leave.
/// </summary>
[DiscordUnknownEventEventSubscriber]
[UsedImplicitly]
internal sealed class AuditLogKickHandler(
    DB db,
    ILogger<AuditLogKickHandler> logger,
    IGuildConfigService guildConfigService)
    : IDiscordUnknownEventEventSubscriber
{
    // Discord audit log action type for MEMBER_KICK.
    private const int MemberKickActionType = 20;

    public async Task DiscordOnUnknownEvent(DiscordClient sender, UnknownEventArgs args)
    {
        if (args.EventName != "GUILD_AUDIT_LOG_ENTRY_CREATE")
        {
            return;
        }

        ulong guildId;
        ulong targetId;
        ulong? actorId = null;
        string reason = null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(args.Json);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("action_type", out JsonElement actionTypeEl) ||
                actionTypeEl.GetInt32() != MemberKickActionType)
            {
                return;
            }

            if (!root.TryGetProperty("guild_id", out JsonElement guildIdEl) ||
                !ulong.TryParse(guildIdEl.GetString(), out guildId))
            {
                logger.LogWarning("GUILD_AUDIT_LOG_ENTRY_CREATE (kick) missing or invalid guild_id");
                return;
            }

            if (!root.TryGetProperty("target_id", out JsonElement targetIdEl) ||
                !ulong.TryParse(targetIdEl.GetString(), out targetId))
            {
                logger.LogWarning("GUILD_AUDIT_LOG_ENTRY_CREATE (kick) missing or invalid target_id");
                return;
            }

            if (root.TryGetProperty("user_id", out JsonElement userIdEl) &&
                ulong.TryParse(userIdEl.GetString(), out ulong parsedActorId))
            {
                actorId = parsedActorId;
            }

            if (root.TryGetProperty("reason", out JsonElement reasonEl))
            {
                reason = reasonEl.GetString();
            }
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse GUILD_AUDIT_LOG_ENTRY_CREATE payload");
            return;
        }

        if (await guildConfigService.GetAsync(guildId) is null)
        {
            return;
        }

        string entityId = $"{guildId}-{targetId}";
        GuildMember member = await db.Find<GuildMember>().OneAsync(entityId);

        if (member is null)
        {
            logger.LogDebug(
                "GUILD_AUDIT_LOG_ENTRY_CREATE (kick): member {TargetId} in guild {GuildId} not found in DB, skipping",
                targetId, guildId);
            return;
        }

        // Only pre-mark when the member is in a non-terminal state. If a terminal status
        // is already set (e.g. the bot panel kicked them), don't overwrite it.
        if (member.Status is not (MemberStatus.New or MemberStatus.Onboarding or
            MemberStatus.QuestionnaireSubmitted or MemberStatus.FullMember or
            MemberStatus.StrangerRoleRemoved or MemberStatus.Unknown))
        {
            logger.LogInformation(
                "GUILD_AUDIT_LOG_ENTRY_CREATE (kick) for {TargetId}: status already {Status}, skipping pre-mark",
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
