using System.Diagnostics.CodeAnalysis;

using IgorBot.Core;

using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Entities;

namespace IgorBot.Schema;

/// <summary>
///     MongoDB entity storing guild configuration. Mirrors <see cref="GuildConfig" />.
/// </summary>
public class GuildConfigEntity : IEntity
{
    /// <summary>
    ///     The unique ID of the guild.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    ///     Stranger role snowflake ID.
    /// </summary>
    public ulong StrangerRoleId { get; set; }

    /// <summary>
    ///     Fully promoted member snowflake ID.
    /// </summary>
    public ulong MemberRoleId { get; set; }

    /// <summary>
    ///     Category snowflake ID where newbie channels should be created under.
    /// </summary>
    public ulong ApplicationCategoryId { get; set; }

    /// <summary>
    ///     Format string to use when naming newbie channels.
    /// </summary>
    public string ApplicationChannelNameFormat { get; set; }

    /// <summary>
    ///     Optional list of snowflake IDs of moderators with permissions to see and interact newbie channels.
    /// </summary>
    public List<ulong> ApplicationModeratorRoleIds { get; set; } = new();

    /// <summary>
    ///     Message template the bot uses to welcome a newbie in their channel.
    /// </summary>
    public string NewbieWelcomeTemplate { get; set; }

    /// <summary>
    ///     Channel snowflake ID where the status embed messages should appear.
    /// </summary>
    public ulong StrangerStatusChannelId { get; set; }

    /// <summary>
    ///     Message template the bot uses to welcome promoted members.
    /// </summary>
    public string MemberWelcomeTemplate { get; set; }

    /// <summary>
    ///     Channel snowflake ID where the welcome messages of promoted members should appear.
    /// </summary>
    public ulong MemberWelcomeMessageChannelId { get; set; }

    /// <summary>
    ///     List of questionnaires the bot offers.
    /// </summary>
    public Dictionary<string, Questionnaire> Questionnaires { get; set; } = new();

    /// <summary>
    ///     Timespan after which stale strangers get auto-kicked, if enabled.
    /// </summary>
    public TimeSpan? IdleKickTimeSpan { get; set; }

    /// <summary>
    ///     Optional channel ID for the spambot honeypot feature.
    /// </summary>
    public ulong? HoneypotChannelId { get; set; }

    /// <summary>
    ///     Role members immune to honeypot actions.
    /// </summary>
    public List<ulong> HoneypotExclusionRoleIds { get; set; } = new();

    /// <summary>
    ///     If true, automatically assign the Stranger role when a member joins.
    /// </summary>
    public bool AutoAssignStrangerRoleOnJoin { get; set; }

    /// <summary>
    ///     If true, run the onboarding workflow when a member gets the stranger role. Null when absent (treated as true).
    /// </summary>
    public bool? EnableOnboardingWorkflow { get; set; }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [BsonId]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public string ID { get; set; }

    object IEntity.GenerateNewID()
    {
        return GenerateNewID();
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public string GenerateNewID()
    {
        return $"{GuildId}";
    }

    public bool HasDefaultID()
    {
        return string.IsNullOrEmpty(ID);
    }

    /// <summary>
    ///     Converts this entity to a <see cref="GuildConfig" /> for use in application logic.
    /// </summary>
    public GuildConfig ToGuildConfig()
    {
        GuildConfig result = new()
        {
            GuildId = GuildId,
            StrangerRoleId = StrangerRoleId,
            MemberRoleId = MemberRoleId,
            ApplicationCategoryId = ApplicationCategoryId,
            ApplicationChannelNameFormat = ApplicationChannelNameFormat ?? "newbie-{0:D4}",
            ApplicationModeratorRoleIds = new List<ulong>(ApplicationModeratorRoleIds),
            NewbieWelcomeTemplate = NewbieWelcomeTemplate,
            StrangerStatusChannelId = StrangerStatusChannelId,
            MemberWelcomeTemplate = MemberWelcomeTemplate,
            MemberWelcomeMessageChannelId = MemberWelcomeMessageChannelId,
            IdleKickTimeSpan = IdleKickTimeSpan,
            HoneypotChannelId = HoneypotChannelId,
            HoneypotExclusionRoleIds = new List<ulong>(HoneypotExclusionRoleIds),
            AutoAssignStrangerRoleOnJoin = AutoAssignStrangerRoleOnJoin,
            EnableOnboardingWorkflow = EnableOnboardingWorkflow ?? true
        };
        result.Questionnaires = new Dictionary<string, Questionnaire>(Questionnaires);
        return result;
    }

    /// <summary>
    ///     Creates an entity from a <see cref="GuildConfig" />.
    /// </summary>
    public static GuildConfigEntity FromGuildConfig(GuildConfig config)
    {
        return new GuildConfigEntity
        {
            GuildId = config.GuildId,
            StrangerRoleId = config.StrangerRoleId,
            MemberRoleId = config.MemberRoleId,
            ApplicationCategoryId = config.ApplicationCategoryId,
            ApplicationChannelNameFormat = config.ApplicationChannelNameFormat ?? "newbie-{0:D4}",
            ApplicationModeratorRoleIds = new List<ulong>(config.ApplicationModeratorRoleIds),
            NewbieWelcomeTemplate = config.NewbieWelcomeTemplate,
            StrangerStatusChannelId = config.StrangerStatusChannelId,
            MemberWelcomeTemplate = config.MemberWelcomeTemplate,
            MemberWelcomeMessageChannelId = config.MemberWelcomeMessageChannelId,
            Questionnaires = new Dictionary<string, Questionnaire>(config.Questionnaires),
            IdleKickTimeSpan = config.IdleKickTimeSpan,
            HoneypotChannelId = config.HoneypotChannelId,
            HoneypotExclusionRoleIds = new List<ulong>(config.HoneypotExclusionRoleIds),
            AutoAssignStrangerRoleOnJoin = config.AutoAssignStrangerRoleOnJoin,
            EnableOnboardingWorkflow = config.EnableOnboardingWorkflow
        };
    }
}