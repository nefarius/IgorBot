using DSharpPlus.Entities.AuditLogs;

using IgorBot.Schema;

namespace IgorBot.Modules;

/// <summary>
///     Target, actor, and reason taken from a native kick audit-log entry.
/// </summary>
internal readonly record struct ExternalKickObservation(ulong TargetId, ulong? ActorId, string? Reason);

/// <summary>
///     Decides whether an observed external kick may overwrite the member's current status.
/// </summary>
internal static class ExternalKickPolicy
{
    public static bool TryRead(DiscordAuditLogKickEntry kick, out ExternalKickObservation observation)
    {
        if (kick.Target is null)
        {
            observation = default;
            return false;
        }

        observation = new ExternalKickObservation(kick.Target.Id, kick.UserResponsible?.Id, kick.Reason);
        return true;
    }

    public static bool ShouldPreMark(MemberStatus status) =>
        status is MemberStatus.New
            or MemberStatus.Onboarding
            or MemberStatus.QuestionnaireSubmitted
            or MemberStatus.FullMember
            or MemberStatus.StrangerRoleRemoved
            or MemberStatus.Unknown;
}
