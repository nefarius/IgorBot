using System;
using AleRoe.LiteDB.Extensions.DependencyInjection;
using DSharpPlus.Interactivity.Enums;
using IgorBot.ApplicationCommands;
using IgorBot.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nefarius.DSharpPlus.Extensions.Hosting;
using Nefarius.DSharpPlus.Interactivity.Extensions.Hosting;
using Nefarius.DSharpPlus.SlashCommands.Extensions.Hosting;
using Serilog;

namespace IgorBot
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    var configuration = new ConfigurationBuilder()
                        .AddJsonFile("appsettings.json", false, true)
                        .Build();

                    var section = configuration.GetSection("Bot");
                    var config = section.Get<IgorConfig>();

                    services.AddSingleton(config);

                    var logger = new LoggerConfiguration()
                        .ReadFrom.Configuration(configuration)
                        .CreateLogger();

                    services.AddLogging(builder =>
                    {
                        builder.SetMinimumLevel(LogLevel.Information);
                        builder.AddSerilog(logger, true);
                    });

                    services.AddSingleton(new LoggerFactory().AddSerilog(logger));

                    services.AddLiteDatabase();

                    services.AddDiscord(discordConfiguration => { discordConfiguration.Token = config.Discord.Token; });

                    services.AddDiscordInteractivity(options =>
                    {
                        options.PaginationBehaviour = PaginationBehaviour.WrapAround;
                        options.ResponseBehavior = InteractionResponseBehavior.Ack;
                        options.ResponseMessage = "That's not a valid button";
                        options.Timeout = TimeSpan.FromMinutes(2);
                    });

                    services.AddDiscordSlashCommands(extension: extension =>
                    {
                        foreach (var guild in config.Guilds)
                            extension.RegisterCommands<OnBoardingApplicationCommands>(ulong.Parse(guild.Key));
                    });

                    services.AddDiscordHostedService();
                });
        }
    }
}