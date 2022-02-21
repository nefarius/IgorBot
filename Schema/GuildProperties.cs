using LiteDB;

namespace IgorBot.Schema
{
    public class GuildProperties
    {
        public ObjectId Id { get; set; } = ObjectId.NewObjectId();

        public ulong GuildId { get; set; }

        public ulong ApplicationChannels { get; set; } = 1;
    }
}