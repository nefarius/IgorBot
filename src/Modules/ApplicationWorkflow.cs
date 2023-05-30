using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

using IgorBot.Core;
using IgorBot.Handlers;
using IgorBot.Schema;

using Microsoft.Extensions.Options;

using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting.Events;

using Rebus.Bus;

namespace IgorBot.Modules;

[DiscordGuildMemberAddedEventSubscriber]
[DiscordGuildMemberUpdatedEventSubscriber]
[DiscordGuildMemberRemovedEventSubscriber]
[DiscordComponentInteractionCreatedEventSubscriber]
internal partial class ApplicationWorkflow :
    IDiscordGuildMemberAddedEventSubscriber,
    IDiscordGuildMemberUpdatedEventSubscriber,
    IDiscordGuildMemberRemovedEventSubscriber,
    IDiscordComponentInteractionCreatedEventSubscriber
{
    private readonly IOptions<IgorConfig> _config;
    private readonly ILogger<ApplicationWorkflow> _logger;
    private readonly IBus _messageBus;

    public ApplicationWorkflow(ILogger<ApplicationWorkflow> logger, IOptions<IgorConfig> config, IBus messageBus)
    {
        _logger = logger;
        _config = config;
        _messageBus = messageBus;
    }

    public async Task DiscordOnComponentInteractionCreated(DiscordClient sender,
        ComponentInteractionCreateEventArgs args)
    {
        string category;
        string dbId;
        string action;

        try
        {
            string[] fields = args.Id.Split("|");
            category = fields[0];
            dbId = fields[1];
            action = fields[2];
        }
        catch (IndexOutOfRangeException ex)
        {
            _logger.LogError(ex, "Failed to parse custom ID");

            _ = Task.Run(async () => await args.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder(
                    new DiscordMessageBuilder().WithContent($"Exception: {ex.Message}"))));
            return;
        }

        GuildConfig guildConfig = _config.Value.Guilds[args.Guild.Id.ToString()];

        _logger.LogDebug("Got {Collection} - {Id} with action {Action}", category, dbId, action);

        await args.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);

        _ = Task.Run(async () =>
        {
            try
            {
                switch (category)
                {
                    case "strangers":

                        _logger.LogDebug("Database ID: {Id}", dbId);

                        GuildMember dbMember = (await DB.Find<GuildMember>()
                                .ManyAsync(m => m.Eq(f => f.Application.ID, dbId)))
                            .FirstOrDefault();

                        if (dbMember is null)
                        {
                            dbMember = (await DB.Find<GuildMember>().OneAsync(dbId));

                            if (dbMember is null)
                            {
                                _logger.LogError("Guild member for this application not found in DB");
                                await args.Interaction.EditOriginalResponseAsync(
                                    new DiscordWebhookBuilder().WithContent("Member entry not found in database!"));
                                break;
                            }
                        }

                        StrangerApplicationEmbed application = dbMember.Application;

                        if (application is null)
                        {
                            _logger.LogError("DB Entry with {Id} not found", dbId);
                            await args.Interaction.EditOriginalResponseAsync(
                                new DiscordWebhookBuilder().WithContent("Application entry not found in database!"));
                            break;
                        }

                        DiscordMember member;

                        try
                        {
                            member = await args.Guild.GetMemberAsync(dbMember.MemberId);
                        }
                        catch (NotFoundException)
                        {
                            await args.Interaction.EditOriginalResponseAsync(
                                new DiscordWebhookBuilder().WithContent("Member not found in Guild!"));
                            break;
                        }

                        switch (action)
                        {
                            case GuildMember.StrangerCommandKick:

                                await HandleStrangerKick(args, sender, dbMember, member);
                                break;

                            case GuildMember.StrangerCommandBan:

                                await HandleStrangerBan(args, sender, dbMember, member);
                                break;

                            case StrangerApplicationEmbed.StrangerCommandPromote:

                                await HandleStrangerPromote(args, sender, dbMember, member, guildConfig);
                                break;

                            case StrangerApplicationEmbed.StrangerCommandDisableAutoKick:

                                await HandleStrangerDisableAutoKick(args, sender, dbMember);
                                break;

                            default:

                                await args.Interaction.EditOriginalResponseAsync(
                                    new DiscordWebhookBuilder().WithContent("Unknown action!"));
                                break;
                        }

                        break;

                    default:
                        await args.Interaction.EditOriginalResponseAsync(
                            new DiscordWebhookBuilder().WithContent("Unknown collection!"));
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in interaction processing");
            }
        });
    }

    private async Task HandleStrangerPromote(ComponentInteractionCreateEventArgs args, DiscordClient client,
        GuildMember entry,
        DiscordMember member, GuildConfig guildConfig)
    {
        entry.PromotedAt = DateTime.UtcNow;

        await entry.SaveAsync();

        _logger.LogInformation("{User} promoted {Member}",
            args.User, member);

        DiscordRole strangerRole = args.Guild.GetRole(guildConfig.StrangerRoleId);
        DiscordRole memberRole = args.Guild.GetRole(guildConfig.MemberRoleId);

        await member.GrantRoleAsync(memberRole);

        await member.RevokeRoleAsync(strangerRole);

        await entry.RespondToInteraction(args, client);

        try
        {
            DiscordChannel welcomeChannel = args.Guild.GetChannel(guildConfig.MemberWelcomeMessageChannelId);

            await welcomeChannel
                .SendMessageAsync(string.Format(guildConfig.MemberWelcomeTemplate, member.Mention));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver welcome message");
        }
    }

    private async Task HandleStrangerBan(ComponentInteractionCreateEventArgs args, DiscordClient client,
        GuildMember entry,
        DiscordMember member)
    {
        _logger.LogInformation("{User} banned {Member}",
            args.User, member);

        await member.BanAsync();

        entry.RemovedByModeration = true;
        entry.BannedAt = DateTime.UtcNow;

        await entry.SaveAsync();

        await entry.RespondToInteraction(args, client);
    }

    private async Task HandleStrangerKick(ComponentInteractionCreateEventArgs args, DiscordClient client,
        GuildMember entry,
        DiscordMember member)
    {
        _logger.LogInformation("{User} kicked {Member}",
            args.User, member);

        await member.RemoveAsync();

        entry.RemovedByModeration = true;
        entry.KickedAt = DateTime.UtcNow;

        await entry.SaveAsync();

        await entry.RespondToInteraction(args, client);
    }

    private async Task HandleStrangerDisableAutoKick(
        ComponentInteractionCreateEventArgs args,
        DiscordClient client,
        GuildMember entry)
    {
        _logger.LogInformation("Disabling auto-kick for {Member}", entry);

        entry.Application!.IsAutoKickEnabled = false;
        await entry.Application.SaveAsync();

        await entry.SaveAsync();

        await entry.RespondToInteraction(args, client);
    }
}