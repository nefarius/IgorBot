using System.Diagnostics.CodeAnalysis;

using DSharpPlus;
using DSharpPlus.Entities;

namespace IgorBot.Core;

/// <summary>
///     Discord bot configuration.
/// </summary>
[SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
internal sealed class DiscordConfig
{
    /// <summary>
    ///     Bot token.
    /// </summary>
    public string Token { get; set; }
}

/// <summary>
///     Configuration for a guild.
/// </summary>
[SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
internal sealed class GuildConfig
{
    /// <summary>
    ///     Stranger role snowflake ID.
    /// </summary>
    public ulong StrangerRoleId { get; set; }

    /// <summary>
    ///     Fully promoted member snowflake ID.
    /// </summary>
    public ulong MemberRoleId { get; set; }

    /// <summary>
    ///     Guild snowflake ID.
    /// </summary>
    public ulong GuildId { get; set; }

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
    public Dictionary<string, Questionnaire> Questionnaires { get; } = new();

    /// <summary>
    ///     Timespan after which stale strangers get auto-kicked, if enabled.
    /// </summary>
    public TimeSpan? IdleKickTimeSpan { get; set; }
}

/// <summary>
///     A question to ask.
/// </summary>
[SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public sealed class Question
{
    /// <summary>
    ///     Content of the question.
    /// </summary>
    public string Content { get; set; }
}

/// <summary>
///     A button component to place on the submission.
/// </summary>
[SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public sealed class SubmissionActionButton
{
    /// <summary>
    ///     The <see cref="DiscordButtonStyle" />.
    /// </summary>
    public DiscordButtonStyle Style { get; set; }

    /// <summary>
    ///     The visible button label.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    ///     If disabled, the button won't be click-able.
    /// </summary>
    public bool IsDisabled { get; set; } = false;

    /// <summary>
    ///     Custom ID used in interaction event callback.
    /// </summary>
    public string CustomId { get; set; }
}

/// <summary>
///     A set of one or more questions the user can respond with answers to which results get posted into a submission
///     channel.
/// </summary>
[SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public sealed class Questionnaire
{
    /// <summary>
    ///     Friendly name identifying this questionnaire.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     Unique ID.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    ///     An optional description.
    /// </summary>
    public string Description { get; set; } = "<no description>";

    /// <summary>
    ///     Collection of one or more questions.
    /// </summary>
    public List<Question> Questions { get; set; } = new();

    /// <summary>
    ///     The Channel ID to send the submission to.
    /// </summary>
    public ulong SubmissionChannelId { get; set; }

    /// <summary>
    ///     Timeout value in minutes the user has to complete a response.
    /// </summary>
    public ulong TimeoutMinutes { get; set; } = 3;

    /// <summary>
    ///     Optional set of action buttons to be placed on submission embed.
    /// </summary>
    public List<SubmissionActionButton> ActionButtons { get; set; } = new();

    /// <summary>
    ///     If true, start it in a DM channel with the invoking user.
    /// </summary>
    public bool ConductInPrivate { get; set; } = false;

    /// <summary>
    ///     Optional list of channel IDs where the command should be denied.
    /// </summary>
    public IList<ulong> BlockedChannelIds { get; set; } = new List<ulong>();
}

[SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
internal sealed class IgorConfig
{
    public DiscordConfig Discord { get; set; }

    public Dictionary<string, GuildConfig> Guilds { get; set; } = new();
}