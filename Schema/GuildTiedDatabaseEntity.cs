namespace IgorBot.Schema
{
    /// <summary>
    ///     Provides a template for objects tied to a <see cref="DiscordGuild" />.
    /// </summary>
    public abstract class GuildTiedDatabaseEntity
    {
        /// <summary>
        ///     The Snowflake ID of this object in the Discord environment.
        /// </summary>
        public ulong DiscordId { get; set; }

        /// <summary>
        ///     The Snowflake ID of the <see cref="DiscordGuild" /> this object belongs to.
        /// </summary>
        public ulong GuildId { get; set; }
    }
}