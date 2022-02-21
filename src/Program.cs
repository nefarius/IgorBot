using Coravel;

using DSharpPlus;
using DSharpPlus.Interactivity.Enums;

using IgorBot.ApplicationCommands;
using IgorBot.Core;
using IgorBot.Invocables;

using MongoDB.Driver;
using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting;
using Nefarius.DSharpPlus.Interactivity.Extensions.Hosting;
using Nefarius.DSharpPlus.SlashCommands.Extensions.Hosting;

using Serilog;
using Serilog.Core;

IHostBuilder builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        IConfigurationSection section = hostContext.Configuration.GetSection("Bot");
        IgorConfig config = section.Get<IgorConfig>();

        if (!config.Guilds.Any())
        {
            throw new InvalidOperationException("No Guilds found in configuration!");
        }

        services.Configure<IgorConfig>(section);

        string connectionString = hostContext.Configuration.GetConnectionString("MongoDB");

        DB.InitAsync("IgorBot", MongoClientSettings.FromConnectionString(connectionString))
            .GetAwaiter()
            .GetResult();

        ConfigureLogging(hostContext, services);

        ConfigureScheduler(services);

        ConfigureDiscord(services, config);
    });

IHost host = builder.Build();

host.Services.UseScheduler(scheduler =>
    {
        scheduler
            .Schedule<KickStaleInvokable>()
            .EveryMinute();
    }
);

host.Run();


void ConfigureDiscord(IServiceCollection serviceCollection, IgorConfig igorConfig)
{
    serviceCollection.AddDiscord(discordConfiguration =>
    {
        discordConfiguration.Token = igorConfig.Discord.Token;
        discordConfiguration.MinimumLogLevel = LogLevel.Debug;
        discordConfiguration.Intents = DiscordIntents.GuildMessages |
                                       DiscordIntents.DirectMessages |
                                       DiscordIntents.MessageContents;
    });

    serviceCollection.AddDiscordInteractivity(options =>
    {
        options.PaginationBehaviour = PaginationBehaviour.WrapAround;
        options.ResponseBehavior = InteractionResponseBehavior.Ack;
        options.ResponseMessage = "That's not a valid button";
        options.Timeout = TimeSpan.FromMinutes(3);
    });

    serviceCollection.AddDiscordSlashCommands(extension: extension =>
    {
        extension.RegisterCommands<OnBoardingApplicationCommands>();
    });

    serviceCollection.AddDiscordHostedService();

    serviceCollection.AddSingleton<Global>();
    serviceCollection.AddHostedService<GlobalService>();
}

void ConfigureLogging(HostBuilderContext hostBuilderContext, IServiceCollection serviceCollection)
{
    Logger logger = new LoggerConfiguration()
        .ReadFrom.Configuration(hostBuilderContext.Configuration)
        .CreateLogger();

    serviceCollection.AddLogging(b =>
    {
        b.SetMinimumLevel(LogLevel.Information);
        b.AddSerilog(logger, true);
    });

    serviceCollection.AddSingleton(new LoggerFactory().AddSerilog(logger));

    Log.Logger = logger;
}

void ConfigureScheduler(IServiceCollection serviceCollection)
{
    serviceCollection.AddScheduler();

    serviceCollection.AddTransient<KickStaleInvokable>();
}