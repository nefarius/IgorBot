using System.Threading.Channels;

using Coravel;

using IgorBot.Core;
using IgorBot.Handlers;
using IgorBot.Invocables;
using IgorBot.Services;

using MongoDB.Driver;
using MongoDB.Entities;

using Serilog;
using Serilog.Core;

IHostBuilder builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        IConfigurationSection section = hostContext.Configuration.GetSection("Bot");
        services.Configure<IgorConfig>(section);

        // Configure Serilog first so the static Log.Logger is set before any migration
        // code runs; both GuildConfigMigration and GuildMemberStatusMigration log via Log.*.
        ConfigureLogging(hostContext, services);

        string? connectionString = hostContext.Configuration.GetConnectionString("MongoDB");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:MongoDB is required in configuration but is missing or empty.");
        }

        DB db = DB.InitAsync("IgorBot", MongoClientSettings.FromConnectionString(connectionString))
            .GetAwaiter()
            .GetResult();

        services.AddSingleton(db);
        GuildConfigMigration.Run(hostContext.Configuration, db);

        bool migrationDryRun = hostContext.Configuration.GetValue<bool>("Bot:Migration:DryRun");
        GuildMemberStatusMigration.RunAsync(db, migrationDryRun).GetAwaiter().GetResult();

        services.AddSingleton<IDiscordReadinessService, DiscordReadinessService>();
        services.AddSingleton<IHoneypotBanClient, DiscordHoneypotBanClient>();
        services.AddSingleton<IHoneypotEnforcementService, HoneypotEnforcementService>();

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

        ConfigureScheduler(services);

        IgorConfig? config = section.Get<IgorConfig>();
        if (config?.Discord?.Token == null)
        {
            throw new InvalidOperationException("Bot:Discord:Token is required in configuration.");
        }

        IgorDiscordRegistration.AddIgorDiscord(services, config.Discord.Token);

        services.AddHostedService<StartupTasks>();
    });

IHost host = builder.Build();

host.Services.UseScheduler(scheduler =>
    {
        scheduler
            .Schedule<KickStaleInvokable>()
            .EveryMinute();

        scheduler
            .Schedule<OrphanEmbedReconciliationInvokable>()
            .Daily();

        scheduler
            .Schedule<HoneypotReconciliationInvokable>()
            .EveryMinute()
            .PreventOverlapping(nameof(HoneypotReconciliationInvokable));
    }
);

host.Run();
return;


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
    serviceCollection.AddTransient<OrphanEmbedReconciliationInvokable>();
    serviceCollection.AddTransient<HoneypotReconciliationInvokable>();
}