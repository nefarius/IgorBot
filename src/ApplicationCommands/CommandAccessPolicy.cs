namespace IgorBot.ApplicationCommands;

/// <summary>
///     Documents which Discord permissions the slash commands require.
///     The live check is <c>RequirePermissionsAttribute</c>; this policy is the testable contract.
/// </summary>
internal static class CommandAccessPolicy
{
    public const string Administrator = "Administrator";
    public const string SendMessages = "SendMessages";

    public static IReadOnlyList<string> ConfigPermissions { get; } = [Administrator];

    public static IReadOnlyList<string> ApplyMemberPermissions { get; } = [SendMessages];

    public static bool IsAuthorized(IEnumerable<string> granted, IReadOnlyList<string> required) =>
        required.All(granted.Contains);
}
