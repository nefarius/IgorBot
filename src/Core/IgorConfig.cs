using System.Diagnostics.CodeAnalysis;

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
    public string Token { get; set; } = null!;
}

/// <summary>
///     Configuration for a guild.
/// </summary>
[SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public sealed class GuildConfig
{
    private Dictionary<string, Questionnaire> _questionnaires = new();

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
    public string? ApplicationChannelNameFormat { get; set; }

    /// <summary>
    ///     Optional list of snowflake IDs of moderators with permissions to see and interact newbie channels.
    /// </summary>
    public List<ulong> ApplicationModeratorRoleIds { get; set; } = new();

    /// <summary>
    ///     Message template the bot uses to welcome a newbie in their channel.
    /// </summary>
    public string? NewbieWelcomeTemplate { get; set; }

    /// <summary>
    ///     Channel snowflake ID where the status embed messages should appear.
    /// </summary>
    public ulong StrangerStatusChannelId { get; set; }

    /// <summary>
    ///     Message template the bot uses to welcome promoted members.
    /// </summary>
    public string? MemberWelcomeTemplate { get; set; }

    /// <summary>
    ///     Channel snowflake ID where the welcome messages of promoted members should appear.
    /// </summary>
    public ulong MemberWelcomeMessageChannelId { get; set; }

    /// <summary>
    ///     List of questionnaires the bot offers.
    /// </summary>
    public Dictionary<string, Questionnaire> Questionnaires
    {
        get => _questionnaires;
        set => _questionnaires = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    ///     Timespan after which stale strangers get auto-kicked, if enabled.
    /// </summary>
    public TimeSpan? IdleKickTimeSpan { get; set; }

    /// <summary>
    ///     Optional channel ID for the spambot honeypot feature.
    /// </summary>
    public ulong? HoneypotChannelId { get; set; }

    /// <summary>
    ///     UTC time <see cref="HoneypotChannelId" /> was last set or changed. Null when unset or unknown.
    /// </summary>
    public DateTime? HoneypotChannelActivatedAt { get; set; }

    /// <summary>
    ///     Role members immune to <see cref="HoneypotChannelId" /> actions.
    /// </summary>
    public List<ulong> HoneypotExclusionRoleIds { get; set; } = new();

    /// <summary>
    ///     If true, automatically assign the Stranger role when a member joins. When enabled, a 2nd bot is not required.
    /// </summary>
    public bool AutoAssignStrangerRoleOnJoin { get; set; } = false;

    /// <summary>
    ///     If true, run the onboarding workflow (channel creation, status widget, questionnaire) when a member gets the
    ///     stranger role. When false, only role assignment happens if <see cref="AutoAssignStrangerRoleOnJoin" /> is set.
    /// </summary>
    public bool EnableOnboardingWorkflow { get; set; } = true;

    /// <summary>
    ///     Version of the stored document this snapshot was loaded from. <c>0</c> means "not loaded from storage":
    ///     saving then overwrites. A non-zero value makes <c>SaveAsync</c> fail with
    ///     <see cref="ConfigConflictException" /> when somebody else saved in the meantime.
    /// </summary>
    public int ConfigVersion { get; set; }

    /// <summary>
    ///     Explicit marker for an intentional overwrite of an existing stored configuration (fresh
    ///     <c>/config setup</c>). Not persisted. Without it, <c>SaveAsync</c> always compares
    ///     <see cref="ConfigVersion" /> (including <c>0</c> for legacy documents) against the stored one.
    /// </summary>
    public bool OverwriteExisting { get; set; }
}

/// <summary>
///     Thrown when a guild configuration snapshot was saved after a newer version had been stored.
/// </summary>
public sealed class ConfigConflictException(ulong guildId)
    : InvalidOperationException($"Guild config {guildId} was changed concurrently; reload and retry.")
{
    public ulong GuildId { get; } = guildId;
}

/// <summary>
///     Bot-wide connection settings for a System One decision endpoint.
/// </summary>
internal sealed class SystemOneConfig
{
    /// <summary>
    ///     Server root, for example <c>http://192.168.30.187:8081</c>. Empty disables the integration.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    ///     Model name sent to Ollama-compatible servers. PS5LM ignores it and uses the loaded model.
    /// </summary>
    public string Model { get; set; } = "loaded";

    /// <summary>
    ///     Optional bearer token. Prefer <c>Bot__SystemOne__ApiToken</c> over storing this in a file.
    /// </summary>
    public string? ApiToken { get; set; }

    /// <summary>
    ///     Per-request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;
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
    public required string Content { get; set; }
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
    public required string Label { get; set; }

    /// <summary>
    ///     If disabled, the button won't be click-able.
    /// </summary>
    public bool IsDisabled { get; set; } = false;

    /// <summary>
    ///     Custom ID used in interaction event callback.
    /// </summary>
    public required string CustomId { get; set; }
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
    public required string Name { get; set; }

    /// <summary>
    ///     Unique ID.
    /// </summary>
    public required string Id { get; set; }

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

    /// <summary>
    ///     Optional automatic screening for submitted answers.
    /// </summary>
    public QuestionnaireAutomation Automation
    {
        get => _automation ??= new QuestionnaireAutomation();
        set => _automation = value ?? new QuestionnaireAutomation();
    }

    private QuestionnaireAutomation? _automation = new();
}

/// <summary>
///     Well-known questionnaire dictionary keys.
/// </summary>
public static class QuestionnaireKeys
{
    /// <summary>
    ///     The membership application questionnaire.
    /// </summary>
    public const string Member = "Member";
}

/// <summary>
///     Opt-in policy for classifying one questionnaire with System One.
/// </summary>
public sealed class QuestionnaireAutomation
{
    /// <summary>
    ///     When false, submissions follow the existing moderator workflow.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Short description of the server and questionnaire, included in the decision request.
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    ///     Minimum <c>normal</c> probability required before automatic promotion.
    /// </summary>
    public double AutoPromoteNormalProbability { get; set; } = 0.80;
}

[SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
internal sealed class IgorConfig
{
    public DiscordConfig Discord { get; set; } = null!;

    public Dictionary<string, GuildConfig> Guilds { get; set; } = new();

    public SystemOneConfig SystemOne { get; set; } = new();
}