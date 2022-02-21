using System.Collections.Generic;
using DSharpPlus;

namespace IgorBot.Core
{
    internal class DiscordConfig
    {
        public string Token { get; set; }

        public string CommandPrefix { get; set; }
    }

    internal class GuildConfig
    {
        public ulong StrangerRoleId { get; set; }

        public ulong MemberRoleId { get; set; }

        public ulong GuildId { get; set; }

        public ulong ApplicationCategoryId { get; set; }

        public string ApplicationChannelNameFormat { get; set; }

        public List<ulong> ApplicationModeratorRoleIds { get; set; } = new();

        public string NewbieWelcomeTemplate { get; set; }

        public Dictionary<string, Questionnaire> Questionnaires { get; set; } = new();
    }

    /// <summary>
    ///     A question to ask.
    /// </summary>
    public class Question
    {
        /// <summary>
        ///     Content of the question.
        /// </summary>
        public string Content { get; set; }
    }

    /// <summary>
    ///     A button component to place on the submission.
    /// </summary>
    public class SubmissionActionButton
    {
        /// <summary>
        ///     The <see cref="ButtonStyle" />.
        /// </summary>
        public ButtonStyle Style { get; set; }

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
    public class Questionnaire
    {
        /// <summary>
        ///     Friendly name identifying this questionnaire.
        /// </summary>
        public string Name { get; set; }

        public string Id { get; set; }

        /// <summary>
        ///     An optional description.
        /// </summary>
        public string Description { get; set; } = "<no description>";

        /// <summary>
        ///     Collection of one or more questions.
        /// </summary>
        public List<Question> Questions { get; set; } = new List<Question>();

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
        public List<SubmissionActionButton> ActionButtons { get; set; } = new List<SubmissionActionButton>();

        /// <summary>
        ///     If true, start it in a DM channel with the invoking user.
        /// </summary>
        public bool ConductInPrivate { get; set; } = false;

        /// <summary>
        ///     Optional list of channel IDs where the command should be denied.
        /// </summary>
        public IList<ulong> BlockedChannelIds { get; set; } = new List<ulong>();
    }

    internal class IgorConfig
    {
        public DiscordConfig Discord { get; set; }

        public Dictionary<string, GuildConfig> Guilds { get; set; } = new();
    }
}