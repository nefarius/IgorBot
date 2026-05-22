namespace IgorBot.Schema;

/// <summary>
///     Canonical lifecycle state of a guild member being tracked by Igor.
/// </summary>
public enum MemberStatus
{
    // IMPORTANT: ordinals are persisted in MongoDB as integers.
    // Never change an existing value, and always assign an explicit value to new members
    // to prevent silent reordering from corrupting stored data.

    /// <summary>
    ///     Not yet determined (default value; used to detect un-migrated documents).
    /// </summary>
    Unknown = 0,

    /// <summary>
    ///     Member joined the guild; no stranger role assigned yet or onboarding not started.
    /// </summary>
    New = 1,

    /// <summary>
    ///     Member has the stranger role and an active newbie channel + application widget.
    /// </summary>
    Onboarding = 2,

    /// <summary>
    ///     Member completed the questionnaire and is awaiting moderator review.
    /// </summary>
    QuestionnaireSubmitted = 3,

    /// <summary>
    ///     Member was promoted to full member.
    /// </summary>
    FullMember = 4,

    /// <summary>
    ///     Member left the guild voluntarily.
    /// </summary>
    LeftVoluntarily = 5,

    /// <summary>
    ///     Member was kicked via the bot's moderator panel.
    /// </summary>
    KickedByModerator = 6,

    /// <summary>
    ///     Member was banned via the bot's moderator panel.
    /// </summary>
    BannedByModerator = 7,

    /// <summary>
    ///     Member was automatically kicked by the idle timer.
    /// </summary>
    AutoKicked = 8,

    /// <summary>
    ///     Member was banned by the honeypot feature.
    /// </summary>
    BannedByHoneypot = 9,

    /// <summary>
    ///     Member was banned outside the bot (Discord UI or another bot) — inferred from GuildBanAdd.
    /// </summary>
    BannedExternally = 10,

    /// <summary>
    ///     Member was kicked outside the bot (Discord UI or another bot) — inferred from audit log.
    /// </summary>
    KickedExternally = 11,

    /// <summary>
    ///     The stranger role was removed from the member while they remained in the guild
    ///     (e.g. manual role removal by a moderator, not via the bot's promote flow).
    /// </summary>
    StrangerRoleRemoved = 12
}
