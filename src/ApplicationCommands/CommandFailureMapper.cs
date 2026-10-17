using DSharpPlus.Commands.EventArgs;

namespace IgorBot.ApplicationCommands;

internal readonly record struct CommandFailure(
    string CommandName,
    ulong? GuildId,
    ulong? UserId,
    string ExceptionType,
    string Message);

/// <summary>
///     Maps a command failure onto the fields written to the log.
/// </summary>
internal static class CommandFailureMapper
{
    public static CommandFailure From(string? commandName, ulong? guildId, ulong? userId, Exception exception) =>
        new(
            string.IsNullOrWhiteSpace(commandName) ? "<unknown>" : commandName,
            guildId,
            userId,
            exception.GetType().Name,
            exception.Message);

    public static CommandFailure From(CommandErroredEventArgs args)
    {
        DSharpPlus.Commands.CommandContext? context = args.Context;
        return From(context?.Command.Name, context?.Guild?.Id, context?.User.Id, args.Exception);
    }
}
