namespace IgorBot.Modules;

/// <summary>
///     The three fields encoded in a moderation widget custom ID: <c>category|databaseId|action</c>.
/// </summary>
internal readonly record struct ComponentInteractionRoute(string Category, string DatabaseId, string Action);

/// <summary>
///     Parses moderation-widget custom IDs without touching a Discord interaction.
/// </summary>
internal static class ComponentInteractionParser
{
    public static bool TryParse(string? customId, out ComponentInteractionRoute route)
    {
        route = default;
        if (string.IsNullOrEmpty(customId))
        {
            return false;
        }

        string[] fields = customId.Split('|');
        if (fields.Length < 3)
        {
            return false;
        }

        route = new ComponentInteractionRoute(fields[0], fields[1], fields[2]);
        return true;
    }
}
