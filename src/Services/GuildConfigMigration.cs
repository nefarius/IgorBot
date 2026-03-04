using IgorBot.Core;
using IgorBot.Schema;
using Microsoft.Extensions.Configuration;
using MongoDB.Entities;
using Serilog;

namespace IgorBot.Services;

/// <summary>
///     One-time migration of guild configuration from appsettings to MongoDB.
/// </summary>
internal static class GuildConfigMigration
{
    public static void Run(IConfiguration configuration, DB db)
    {
        IConfigurationSection? botSection = configuration.GetSection("Bot:Guilds");

        if (!botSection.Exists())
        {
            return;
        }

        Dictionary<string, GuildConfig>? guilds = configuration.GetSection("Bot").Get<IgorConfig>()?.Guilds;

        if (guilds is null || guilds.Count == 0)
        {
            return;
        }

        foreach ((string guildIdStr, GuildConfig? guildConfig) in guilds)
        {
            if (guildConfig is null)
            {
                continue;
            }

            if (guildConfig.GuildId == 0 && ulong.TryParse(guildIdStr, out ulong parsedId))
            {
                guildConfig.GuildId = parsedId;
            }

            GuildConfigEntity? existing = db.Find<GuildConfigEntity>()
                .OneAsync(guildIdStr)
                .GetAwaiter()
                .GetResult();

            if (existing is not null)
            {
                Log.Debug("Guild {GuildId} already in MongoDB, skipping migration", guildIdStr);
                continue;
            }

            GuildConfigEntity entity = GuildConfigEntity.FromGuildConfig(guildConfig);
            entity.ID = guildIdStr;
            db.SaveAsync(entity).GetAwaiter().GetResult();

            Log.Information("Migrated guild {GuildId} from appsettings to MongoDB", guildIdStr);
        }
    }
}
