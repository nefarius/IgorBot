using System.Diagnostics.CodeAnalysis;

using DSharpPlus;
using DSharpPlus.Entities;

using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Entities;

namespace IgorBot.Schema;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
internal sealed class StrangerApplicationEmbed : IEntity
{
    internal const string StrangerCommandPromote = "promote";
    internal const string StrangerCommandDisableAutoKick = "disable-auto-kick";

    /// <summary>
    ///     Snowflake ID of the Guild.
    /// </summary>
    public ulong GuildId { get; init; }

    /// <summary>
    ///     Snowflake ID of the channel the status message widget is in.
    /// </summary>
    public ulong ChannelId { get; init; }

    /// <summary>
    ///     Snowflake ID of status message widget.
    /// </summary>
    public ulong MessageId { get; init; }

    /// <summary>
    ///     Creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     True to auto-kick stale members, false otherwise.
    /// </summary>
    public bool IsAutoKickEnabled { get; set; } = true;

    /// <summary>
    ///     If set, the timestamp at which the member submitted a questionnaire.
    /// </summary>
    public DateTime? QuestionnaireSubmittedAt { get; set; }

    /// <summary>
    ///     Gets a list of button components for actions on this embed.
    /// </summary>
    public IEnumerable<DiscordButtonComponent> ButtonComponents
    {
        get
        {
            List<DiscordButtonComponent> components = new();

            if (QuestionnaireSubmittedAt.HasValue)
            {
                components.Add(new DiscordButtonComponent(
                    ButtonStyle.Success,
                    $"strangers|{ID}|{StrangerCommandPromote}",
                    "Promote"
                ));
            }

            if (IsAutoKickEnabled)
            {
                components.Add(new DiscordButtonComponent(
                    ButtonStyle.Secondary,
                    $"strangers|{ID}|{StrangerCommandDisableAutoKick}",
                    "Disable auto-kick"
                ));
            }

            return components;
        }
    }

    public string GenerateNewID()
    {
        return $"{GuildId}-{ChannelId}-{MessageId}";
    }

    [BsonId]
    public string ID { get; set; }
}