using System;

namespace IgorBot.Schema
{
    /// <summary>
    ///     Contains the relation between a stranger and their application channel.
    /// </summary>
    public class NewbieChannel : GuildTiedDatabaseEntity
    {
        /// <summary>
        ///     Creation timestamp.
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        ///     The ID identifying the channel.
        /// </summary>
        public ulong ChannelId { get; set; }

        /// <summary>
        ///     Display name of the channel.
        /// </summary>
        public string ChannelName { get; set; }

        public override string ToString()
        {
            return $"Channel {ChannelName} ({ChannelId}, Member {DiscordId})";
        }
    }
}