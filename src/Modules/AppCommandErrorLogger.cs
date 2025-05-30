using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.EventArgs;

using JetBrains.Annotations;

using Nefarius.DSharpPlus.SlashCommands.Extensions.Hosting.Attributes;
using Nefarius.DSharpPlus.SlashCommands.Extensions.Hosting.Events;

namespace IgorBot.Modules;

[DiscordSlashCommandsEventsSubscriber]
[UsedImplicitly]
internal class AppCommandErrorLogger(ILogger<AppCommandErrorLogger> logger) : IDiscordSlashCommandsEventsSubscriber
{
    public Task SlashCommandsOnContextMenuErrored(SlashCommandsExtension sender,
        ContextMenuErrorEventArgs args)
    {
        logger.LogError(args.Exception, "Context menu error");

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
        logger.LogError(args.Exception, "Application command error");

        return Task.CompletedTask;
    }

    public Task SlashCommandsOnSlashCommandExecuted(SlashCommandsExtension sender,
        SlashCommandExecutedEventArgs args)
    {
        return Task.CompletedTask;
    }
}