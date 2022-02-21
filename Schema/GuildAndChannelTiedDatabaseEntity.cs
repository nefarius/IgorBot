namespace IgorBot.Schema
{
    public abstract class GuildAndChannelTiedDatabaseEntity : GuildTiedDatabaseEntity
    {
        /// <summary>
        ///     The Snowflake ID of the <see cref="DiscordChannel" /> this object belongs to.
        /// </summary>
        public ulong ChannelId { get; set; }
    }
}