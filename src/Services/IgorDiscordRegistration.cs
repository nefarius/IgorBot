using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Extensions;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;

using IgorBot.ApplicationCommands;
using IgorBot.Modules;

using Microsoft.Extensions.DependencyInjection;

namespace IgorBot.Services;

internal static class IgorDiscordRegistration
{
    public static readonly DiscordIntents GatewayIntents =
        DiscordIntents.Guilds |
        DiscordIntents.GuildMembers |
        DiscordIntents.GuildMessages |
        DiscordIntents.DirectMessages |
        DiscordIntents.MessageContents;

    public static void AddIgorDiscord(IServiceCollection services, string token)
    {
        services.AddDiscordClient(token, GatewayIntents);

        services.AddInteractivityExtension(new InteractivityConfiguration
        {
            PaginationBehaviour = PaginationBehaviour.WrapAround,
            ResponseBehavior = InteractionResponseBehavior.Ack,
            ResponseMessage = "That's not a valid button",
            Timeout = TimeSpan.FromMinutes(3)
        });

        services.AddCommandsExtension((provider, extension) =>
        {
            extension.AddProcessor(new SlashCommandProcessor());
            extension.AddCommands(typeof(OnBoardingApplicationCommands));
            extension.AddCommands(typeof(ConfigCommands));

            ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("IgorBot.Commands");
            extension.CommandErrored += (_, args) =>
            {
                CommandFailure failure = CommandFailureMapper.From(args);
                logger.LogError(
                    args.Exception,
                    "Application command {Command} failed for user {UserId} in guild {GuildId}: {Message}",
                    failure.CommandName,
                    failure.UserId,
                    failure.GuildId,
                    failure.Message);
                return Task.CompletedTask;
            };
        }, new CommandsConfiguration
        {
            RegisterDefaultCommandProcessors = false,
            UseDefaultCommandErrorHandler = false
        });

        services.ConfigureEventHandlers(handlers =>
        {
            handlers.AddEventHandlers<ApplicationWorkflow>(ServiceLifetime.Singleton);
            handlers.AddEventHandlers<HoneypotModule>(ServiceLifetime.Singleton);
            handlers.AddEventHandlers<AuditLogKickHandler>(ServiceLifetime.Singleton);
            handlers.AddEventHandlers<IgorCoreModule>(ServiceLifetime.Singleton);
        });

        services.AddSingleton<IDiscordGateway, DiscordClientGateway>();
        services.AddHostedService<DiscordGatewayHostedService>();
    }
}
