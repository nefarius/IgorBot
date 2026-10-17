namespace IgorBot.Schema;

internal enum ModerationButtonStyle
{
    Secondary,
    Danger,
    Success
}

/// <summary>
///     App-owned description of one moderation button. Discord component objects are built from this.
/// </summary>
internal readonly record struct ModerationButton(
    ModerationButtonStyle Style,
    string CustomId,
    string Label,
    bool Enabled = true);

/// <summary>
///     Button rows shown on stranger status widgets.
/// </summary>
internal static class ModerationWidgetSpec
{
    public static IReadOnlyList<ModerationButton> MemberActions(string memberId) =>
    [
        new(ModerationButtonStyle.Secondary, $"strangers|{memberId}|{GuildMember.StrangerCommandKick}", "Kick"),
        new(ModerationButtonStyle.Danger, $"strangers|{memberId}|{GuildMember.StrangerCommandBan}", "Ban")
    ];

    public static IReadOnlyList<ModerationButton> ApplicationActions(
        string applicationId,
        bool questionnaireSubmitted,
        bool autoKickEnabled)
    {
        List<ModerationButton> buttons = [];
        if (questionnaireSubmitted)
        {
            buttons.Add(new ModerationButton(
                ModerationButtonStyle.Success,
                $"strangers|{applicationId}|{StrangerApplicationEmbed.StrangerCommandPromote}",
                "Promote"));
        }

        if (autoKickEnabled)
        {
            buttons.Add(new ModerationButton(
                ModerationButtonStyle.Secondary,
                $"strangers|{applicationId}|{StrangerApplicationEmbed.StrangerCommandDisableAutoKick}",
                "Disable auto-kick"));
        }

        return buttons;
    }

    public static IReadOnlyList<ModerationButton> DisableAll(IEnumerable<ModerationButton> buttons) =>
        buttons.Select(button => button with { Enabled = false }).ToArray();

    public static IReadOnlyList<ModerationButton> EnableAll(IEnumerable<ModerationButton> buttons) =>
        buttons.Select(button => button with { Enabled = true }).ToArray();
}
