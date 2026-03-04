using System.Threading.Channels;

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

using Serilog;
using Serilog.Core;

IHostBuilder builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        IConfigurationSection section = hostContext.Configuration.GetSection("Bot");
        services.Configure<IgorConfig>(section);

        string connectionString = hostContext.Configuration.GetConnectionString("MongoDB");

        DB db = DB.InitAsync("IgorBot", MongoClientSettings.FromConnectionString(connectionString))
            .GetAwaiter()
            .GetResult();

        services.AddSingleton(db);
        GuildConfigMigration.Run(hostContext.Configuration, db);

        services.AddSingleton<GuildConfigService>();
        services.AddSingleton<IGuildConfigService>(sp =>
            new CachedGuildConfigService(
                sp.GetRequiredService<GuildConfigService>(),
                sp.GetRequiredService<ILogger<CachedGuildConfigService>>()));

        // Onboarding queue for serialized new member processing (bounded to apply backpressure)
        Channel<NewMemberMessage> onboardingChannel = Channel.CreateBounded<NewMemberMessage>(
            new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait });
        services.AddSingleton(onboardingChannel);
        services.AddSingleton(onboardingChannel.Reader);
        services.AddSingleton(onboardingChannel.Writer);
        services.AddSingleton<IOnboardingQueue, OnboardingQueue>();
        services.AddSingleton<NewMemberHandler>();
        services.AddHostedService<OnboardingQueueProcessor>();

        ConfigureLogging(hostContext, services);

        ConfigureScheduler(services);

        IgorConfig config = section.Get<IgorConfig>();
        if (config?.Discord?.Token == null)
        {
            throw new InvalidOperationException("Bot:Discord:Token is required in configuration.");
        }
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
        // GuildMembers is a privileged intent: enable "Server Members Intent" in Discord Developer Portal
        discordConfiguration.Intents = DiscordIntents.Guilds |
                                       DiscordIntents.GuildMembers |
                                       DiscordIntents.GuildMessages |
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
        extension.RegisterCommands<ConfigCommands>();
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