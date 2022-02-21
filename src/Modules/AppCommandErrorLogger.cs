using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.EventArgs;

using Nefarius.DSharpPlus.SlashCommands.Extensions.Hosting.Attributes;
using Nefarius.DSharpPlus.SlashCommands.Extensions.Hosting.Events;

namespace IgorBot.Modules;

[DiscordSlashCommandsEventsSubscriber]
internal class AppCommandErrorLogger : IDiscordSlashCommandsEventsSubscriber
{
    private readonly ILogger<AppCommandErrorLogger> _logger;

    public AppCommandErrorLogger(ILogger<AppCommandErrorLogger> logger)
    {
        _logger = logger;
    }

    public Task SlashCommandsOnContextMenuErrored(SlashCommandsExtension sender,
        ContextMenuErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Context menu error");

        return Task.CompletedTask;
    }

    public Task SlashCommandsOnContextMenuExecuted(SlashCommandsExtension sender,
        ContextMenuExecutedEventArgs args)
    {
        return Task.CompletedTask;
    }

    public Task SlashCommandsOnSlashCommandErrored(SlashCommandsExtension sender,
        SlashCommandErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Application command error");

        return Task.CompletedTask;
    }

    public Task SlashCommandsOnSlashCommandExecuted(SlashCommandsExtension sender,
        SlashCommandExecutedEventArgs args)
    {
        return Task.CompletedTask;
    }
}