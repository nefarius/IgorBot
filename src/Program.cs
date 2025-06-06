using Coravel;

using DSharpPlus;
using DSharpPlus.Interactivity.Enums;

using IgorBot.ApplicationCommands;
using IgorBot.Core;
using IgorBot.Handlers;
using IgorBot.Invocables;
using IgorBot.Services;

using MongoDB.Driver;
using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting;
using Nefarius.DSharpPlus.Interactivity.Extensions.Hosting;
using Nefarius.DSharpPlus.SlashCommands.Extensions.Hosting;

using Rebus.Config;
using Rebus.Routing.TypeBased;
using Rebus.Transport.InMem;

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

        // Register handlers 
        services.AutoRegisterHandlersFromAssemblyOf<NewMemberHandler>();

        // Configure and register Rebus
        services.AddRebus(configure => configure
            .Options(o => {
                o.SetNumberOfWorkers(1);
                o.SetMaxParallelism(1);
            })
            .Transport(t => t.UseInMemoryTransport(new InMemNetwork(), "NewMembers"))
            .Routing(r => r.TypeBased().MapAssemblyOf<NewMemberMessage>("NewMembers")));

        ConfigureLogging(hostContext, services);

        ConfigureScheduler(services);

        ConfigureDiscord(services, config);

        services.AddHostedService<StartupTasks>();
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
return;


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
    serviceCollection.AddQueue();

    serviceCollection.AddTransient<KickStaleInvokable>();
    serviceCollection.AddTransient<MemberDbSyncInvokable>();
}