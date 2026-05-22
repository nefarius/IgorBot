namespace IgorBot.Schema;

/// <summary>
///     Canonical lifecycle state of a guild member being tracked by Igor.
/// </summary>
public enum MemberStatus
{
    /// <summary>
    ///     Not yet determined (default value; used to detect un-migrated documents).
    /// </summary>
    Unknown = 0,

    /// <summary>
    ///     Member joined the guild; no stranger role assigned yet or onboarding not started.
    /// </summary>
    New,

    /// <summary>
    ///     Member has the stranger role and an active newbie channel + application widget.
    /// </summary>
    Onboarding,

    /// <summary>
    ///     Member completed the questionnaire and is awaiting moderator review.
    /// </summary>
    QuestionnaireSubmitted,

    /// <summary>
    ///     Member was promoted to full member.
    /// </summary>
    FullMember,

    /// <summary>
    ///     Member left the guild voluntarily.
    /// </summary>
    LeftVoluntarily,

    /// <summary>
    ///     Member was kicked via the bot's moderator panel.
    /// </summary>
    KickedByModerator,

    /// <summary>
    ///     Member was banned via the bot's moderator panel.
    /// </summary>
    BannedByModerator,

    /// <summary>
    ///     Member was automatically kicked by the idle timer.
    /// </summary>
    AutoKicked,

    /// <summary>
    ///     Member was banned by the honeypot feature.
    /// </summary>
    BannedByHoneypot,

    /// <summary>
    ///     Member was banned outside the bot (Discord UI or another bot) — inferred from GuildBanAdd.
    /// </summary>
    BannedExternally,

    /// <summary>
    ///     Member was kicked outside the bot (Discord UI or another bot) — inferred from audit log.
    /// </summary>
    KickedExternally,

    /// <summary>
    ///     The stranger role was removed from the member while they remained in the guild
    ///     (e.g. manual role removal by a moderator, not via the bot's promote flow).
    /// </summary>
    StrangerRoleRemoved
}
