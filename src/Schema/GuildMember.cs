using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using DSharpPlus;
using DSharpPlus.Entities;

using MongoDB.Entities;

namespace IgorBot.Schema;

/// <summary>
///     Stores runtime data of a guild member.
/// </summary>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
internal sealed partial class GuildMember : IEntity, INotifyPropertyChanged
{
    internal const string StrangerCommandKick = "kick";
    internal const string StrangerCommandBan = "ban";

    /// <summary>
    ///     Gets a list of button components for actions on this embed.
    /// </summary>
    private IEnumerable<DiscordButtonComponent> ButtonComponents
    {
        get
        {
            List<DiscordButtonComponent> components = new()
            {
                new DiscordButtonComponent(
                    ButtonStyle.Secondary,
                    $"strangers|{ID}|{StrangerCommandKick}",
                    "Kick"
                ),
                new DiscordButtonComponent(
                    ButtonStyle.Danger,
                    $"strangers|{ID}|{StrangerCommandBan}",
                    "Ban"
                )
            };

            return components;
        }
    }

    public string GenerateNewID()
    {
        return $"{GuildId}-{MemberId}";
    }

    public override string ToString()
    {
        return Member;
    }

    private DiscordWebhookBuilder GetInteractionResponse(DiscordClient client)
    {
        DiscordWebhookBuilder builder = new();

        DiscordEmbedBuilder statusEmbed = GetStatusWidget(client);

        builder.AddEmbed(statusEmbed);

        if (!HasLeftGuild && ButtonComponents.Any())
        {
            builder.AddComponents(ButtonComponents);
        }

        if (Application is not null && Application.ButtonComponents.Any())
        {
            builder.AddComponents(Application.ButtonComponents);
        }

        return builder;
    }

    private DiscordEmbedBuilder GetStatusWidget(DiscordClient client)
    {
        DiscordGuild guild = client.Guilds[GuildId];

        if (guild.Members.ContainsKey(MemberId))
        {
            DiscordMember member = guild.Members[MemberId];

            Member = member.ToString();
            Mention = member.Mention;
        }

        DiscordEmbedBuilder statusEmbed = new()
        {
            Title = $"Stranger {Member}",
            Description = "Stranger status panel",
            Timestamp = DateTimeOffset.UtcNow,
            Footer = new DiscordEmbedBuilder.EmbedFooter
            {
                IconUrl = client.CurrentUser.AvatarUrl, Text = "Onboarding by Igor"
            }
        };

        statusEmbed.AddField("Member", Mention);
        statusEmbed.AddField("Created at", Formatter.Timestamp(CreatedAt));
        statusEmbed.AddField("Joined at", Formatter.Timestamp(JoinedAt));

        if (KickedAt.HasValue)
        {
            statusEmbed.AddField("Kicked at", Formatter.Timestamp(KickedAt.Value));
        }

        if (BannedAt.HasValue)
        {
            statusEmbed.AddField("Banned at", Formatter.Timestamp(BannedAt.Value));
        }

        if (LeftAt.HasValue && LeftAt.Value > JoinedAt && !RemovedByModeration)
        {
            statusEmbed.AddField("Left by themselves at", Formatter.Timestamp(LeftAt.Value));
        }

        return statusEmbed;
    }
}