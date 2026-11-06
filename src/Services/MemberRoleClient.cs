using DSharpPlus;
using DSharpPlus.Entities;

using IgorBot.Core;
using IgorBot.Schema;

namespace IgorBot.Services;

internal sealed record MemberRoleSnapshot(bool IsBot, IReadOnlyCollection<ulong> RoleIds)
{
    public bool IsApplicant(GuildConfig config) => !IsBot && RoleIds.Contains(config.StrangerRoleId) &&
        !RoleIds.Contains(config.MemberRoleId);
}

internal interface IMemberRoleClient
{
    bool IsGuildAvailable(ulong guildId);
    Task<string?> CheckPromotionAsync(GuildConfig config, CancellationToken ct);
    Task<MemberRoleSnapshot> GetAsync(ulong guildId, ulong memberId, CancellationToken ct);
    Task SetRoleAsync(ulong guildId, ulong memberId, ulong roleId, bool grant, CancellationToken ct);
    Task WelcomeAsync(GuildMember entry, GuildConfig config, CancellationToken ct);
}

internal sealed class DiscordMemberRoleClient(DiscordClient discord) : IMemberRoleClient
{
    public bool IsGuildAvailable(ulong guildId) => discord.Guilds.ContainsKey(guildId);

    public async Task<string?> CheckPromotionAsync(GuildConfig config, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!discord.Guilds.TryGetValue(config.GuildId, out DiscordGuild? guild)) return "Guild unavailable";
        if (config.MemberRoleId == 0 || config.StrangerRoleId == 0 || config.MemberRoleId == config.StrangerRoleId)
            return "Configure distinct member and stranger roles";
        if (!guild.Roles.TryGetValue(config.MemberRoleId, out DiscordRole? memberRole) ||
            !guild.Roles.TryGetValue(config.StrangerRoleId, out DiscordRole? strangerRole))
            return "A configured member or stranger role is missing";
        if (memberRole.IsManaged || strangerRole.IsManaged || memberRole.Id == guild.Id || strangerRole.Id == guild.Id)
            return "Member and stranger roles must be ordinary assignable roles";
        DiscordMember bot = await guild.GetMemberAsync(discord.CurrentUser!.Id, true);
        if (!bot.Permissions.HasPermission(DiscordPermission.ManageRoles)) return "Bot needs Manage Roles";
        if (memberRole.Position >= bot.Hierarchy || strangerRole.Position >= bot.Hierarchy)
            return "Move the bot's highest role above the member and stranger roles";
        return null;
    }

    public async Task<MemberRoleSnapshot> GetAsync(ulong guildId, ulong memberId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        DiscordMember member = await discord.Guilds[guildId].GetMemberAsync(memberId, true);
        return new MemberRoleSnapshot(member.IsBot, member.Roles.Select(r => r.Id).ToArray());
    }

    public async Task SetRoleAsync(ulong guildId, ulong memberId, ulong roleId, bool grant, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        DiscordGuild guild = discord.Guilds[guildId];
        DiscordRole role = guild.Roles.TryGetValue(roleId, out DiscordRole? configuredRole)
            ? configuredRole : throw new InvalidOperationException($"Configured role {roleId} is missing.");
        DiscordMember member = await guild.GetMemberAsync(memberId, true);
        if (grant)
        {
            await member.GrantRoleAsync(role);
        }
        else
        {
            await member.RevokeRoleAsync(role);
        }
    }

    public async Task WelcomeAsync(GuildMember entry, GuildConfig config, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(config.MemberWelcomeTemplate))
        {
            return;
        }

        DiscordChannel channel = await discord.Guilds[entry.GuildId].GetChannelAsync(config.MemberWelcomeMessageChannelId);
        await channel.SendMessageAsync(string.Format(config.MemberWelcomeTemplate, entry.Mention));
    }
}
