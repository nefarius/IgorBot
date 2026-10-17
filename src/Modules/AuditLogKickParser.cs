using IgorBot.Schema;

namespace IgorBot.Modules;

internal enum AuditLogKickParseStatus
{
    IgnoredEvent,
    NotKick,
    Malformed,
    MissingGuildId,
    MissingTargetId,
    Kick
}

/// <summary>
///     A kick observed from a <c>GUILD_AUDIT_LOG_ENTRY_CREATE</c> payload.
/// </summary>
internal readonly record struct AuditLogKickDecision(
    AuditLogKickParseStatus Status,
    ulong GuildId = 0,
    ulong TargetId = 0,
    ulong? ActorId = null,
    string? Reason = null);

/// <summary>
///     Parses the raw audit-log gateway payload DSharpPlus 4 could not model.
///     The live handler now consumes the native v5 event; this parser remains the
///     contract for malformed and non-kick payloads.
/// </summary>
internal static class AuditLogKickParser
{
    internal const string EventName = "GUILD_AUDIT_LOG_ENTRY_CREATE";
    internal const int MemberKickActionType = 20;

    public static AuditLogKickDecision Parse(string? eventName, string? json)
    {
        if (!string.Equals(eventName, EventName, StringComparison.Ordinal))
        {
            return new AuditLogKickDecision(AuditLogKickParseStatus.IgnoredEvent);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return new AuditLogKickDecision(AuditLogKickParseStatus.Malformed);
        }

        try
        {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
            System.Text.Json.JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("action_type", out System.Text.Json.JsonElement actionType))
            {
                return new AuditLogKickDecision(AuditLogKickParseStatus.NotKick);
            }

            if (actionType.ValueKind != System.Text.Json.JsonValueKind.Number ||
                !actionType.TryGetInt32(out int actionTypeValue))
            {
                return new AuditLogKickDecision(AuditLogKickParseStatus.Malformed);
            }

            if (actionTypeValue != MemberKickActionType)
            {
                return new AuditLogKickDecision(AuditLogKickParseStatus.NotKick);
            }

            if (!root.TryGetProperty("guild_id", out System.Text.Json.JsonElement guildIdElement) ||
                !ulong.TryParse(guildIdElement.GetString(), out ulong guildId))
            {
                return new AuditLogKickDecision(AuditLogKickParseStatus.MissingGuildId);
            }

            if (!root.TryGetProperty("target_id", out System.Text.Json.JsonElement targetIdElement) ||
                !ulong.TryParse(targetIdElement.GetString(), out ulong targetId))
            {
                return new AuditLogKickDecision(AuditLogKickParseStatus.MissingTargetId);
            }

            ulong? actorId = null;
            if (root.TryGetProperty("user_id", out System.Text.Json.JsonElement userIdElement) &&
                ulong.TryParse(userIdElement.GetString(), out ulong parsedActorId))
            {
                actorId = parsedActorId;
            }

            string? reason = null;
            if (root.TryGetProperty("reason", out System.Text.Json.JsonElement reasonElement))
            {
                reason = reasonElement.GetString();
            }

            return new AuditLogKickDecision(
                AuditLogKickParseStatus.Kick, guildId, targetId, actorId, reason);
        }
        catch (System.Text.Json.JsonException)
        {
            return new AuditLogKickDecision(AuditLogKickParseStatus.Malformed);
        }
    }
}

/// <summary>
///     Decides whether an observed external kick may overwrite the member's current status.
/// </summary>
internal static class ExternalKickPolicy
{
    public static bool ShouldPreMark(MemberStatus status) =>
        status is MemberStatus.New
            or MemberStatus.Onboarding
            or MemberStatus.QuestionnaireSubmitted
            or MemberStatus.FullMember
            or MemberStatus.StrangerRoleRemoved
            or MemberStatus.Unknown;
}
