using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;

using JetBrains.Annotations;

using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting.Events;

namespace IgorBot.Modules;

[DiscordGuildMemberAddedEventSubscriber]
[DiscordGuildMemberUpdatedEventSubscriber]
[DiscordGuildMemberRemovedEventSubscriber]
[DiscordGuildBanAddedEventSubscriber]
[DiscordComponentInteractionCreatedEventSubscriber]
[UsedImplicitly]
internal partial class ApplicationWorkflow(
    DB db,
    ILogger<ApplicationWorkflow> logger,
    IGuildConfigService guildConfigService,
    IOnboardingQueue onboardingQueue)
    :
        IDiscordGuildMemberAddedEventSubscriber,
        IDiscordGuildMemberUpdatedEventSubscriber,
        IDiscordGuildMemberRemovedEventSubscriber,
        IDiscordGuildBanAddedEventSubscriber,
        IDiscordComponentInteractionCreatedEventSubscriber
{
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
            logger.LogError(ex, "Failed to parse custom ID");

            _ = Task.Run(async () => await args.Interaction.CreateResponseAsync(
                    InteractionResponseType.UpdateMessage,
                    new DiscordInteractionResponseBuilder(
                        new DiscordMessageBuilder().WithContent($"Exception: {ex.Message}"))))
                .ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception is not null)
                    {
                        logger.LogError(t.Exception.GetBaseException(),
                            "Failed to send parse-error response to interaction");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            return;
        }

        GuildConfig? guildConfig = await guildConfigService.GetAsync(args.Guild.Id);
        if (guildConfig == null)
        {
            logger.LogWarning("Guild {GuildId} not configured, ignoring component interaction", args.Guild.Id);
            await args.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent("This server is not configured yet.")
                    .AsEphemeral());
            return;
        }

        logger.LogDebug("Got {Collection} - {Id} with action {Action}", category, dbId, action);

        await args.Interaction.CreateResponseAsync(
            InteractionResponseType.UpdateMessage,
            BuildBusyMirror(args.Message));

        _ = Task.Run(async () =>
        {
            try
            {
                switch (category)
                {
                    case "strangers":

                        logger.LogDebug("Database ID: {Id}", dbId);

                        GuildMember? dbMember = (await db.Find<GuildMember>()
                                .ManyAsync(m => m.Eq(f => f.Application!.ID, dbId)))
                            .FirstOrDefault();

                        if (dbMember is null)
                        {
                            dbMember = await db.Find<GuildMember>().OneAsync(dbId);

                            if (dbMember is null)
                            {
                                logger.LogError("Guild member for this application not found in DB");
                                await args.Interaction.EditOriginalResponseAsync(
                                    new DiscordWebhookBuilder().WithContent("Member entry not found in database!"));
                                break;
                            }
                        }

                        StrangerApplicationEmbed? application = dbMember.Application;

                        if (application is null)
                        {
                            logger.LogError("DB Entry with {Id} not found", dbId);
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
                logger.LogError(ex, "Unhandled exception in interaction processing");
                try
                {
                    await args.Interaction.EditOriginalResponseAsync(
                        BuildRestoredWidget(args.Message)
                            .WithContent("An error occurred while processing the action. Please try again."));
                }
                catch (Exception editEx)
                {
                    logger.LogError(editEx, "Failed to restore widget after interaction error");
                }
            }
        }).ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception is not null)
            {
                logger.LogError(t.Exception.GetBaseException(),
                    "Unhandled exception in component interaction background task");
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task HandleStrangerPromote(ComponentInteractionCreateEventArgs args, DiscordClient client,
        GuildMember entry,
        DiscordMember member, GuildConfig guildConfig)
    {
        DiscordRole strangerRole = args.Guild.GetRole(guildConfig.StrangerRoleId);
        DiscordRole memberRole = args.Guild.GetRole(guildConfig.MemberRoleId);

        // Perform Discord mutations before persisting so the DB is only updated
        // when both role operations have actually succeeded.
        await member.GrantRoleAsync(memberRole);

        try
        {
            await member.RevokeRoleAsync(strangerRole);
        }
        catch (Exception ex)
        {
            // Member role was already granted; log and surface the error so the
            // moderator can manually revoke the stranger role.
            logger.LogError(ex, "RevokeRoleAsync failed for {Member} after GrantRoleAsync succeeded; " +
                                "member role has been granted but stranger role was not removed", member);
            throw;
        }

        // Both Discord mutations succeeded — persist the promotion.
        // Note: GuildMemberUpdated also calls TransitionToAsync(FullMember) when the
        // role-add event fires; the guard in TransitionToAsync makes that a no-op here.
        await entry.TransitionToAsync(db, MemberStatus.FullMember, actorId: args.User.Id);

        logger.LogInformation("{User} promoted {Member}",
            args.User, member);

        await entry.RespondToInteraction(args, client);

        if (!string.IsNullOrEmpty(guildConfig.MemberWelcomeTemplate))
        {
            try
            {
                DiscordChannel welcomeChannel = args.Guild.GetChannel(guildConfig.MemberWelcomeMessageChannelId);

                await welcomeChannel
                    .SendMessageAsync(string.Format(guildConfig.MemberWelcomeTemplate, member.Mention));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to deliver welcome message");
            }
        }
        else
        {
            logger.LogWarning("MemberWelcomeTemplate is not configured for guild {GuildId}, skipping welcome message",
                args.Guild.Id);
        }
    }

    private async Task HandleStrangerBan(ComponentInteractionCreateEventArgs args, DiscordClient client,
        GuildMember entry,
        DiscordMember member)
    {
        logger.LogInformation("{User} initiating ban of {Member} (current status {Status})",
            args.User, member, entry.Status);

        // Pre-mark in DB so that the GuildMemberRemoved event that fires immediately
        // after BanAsync sees the correct terminal status even if this process restarts.
        MemberStatus previousStatus = entry.Status;
        await entry.TransitionToAsync(db, MemberStatus.BannedByModerator,
            reason: args.User.ToString(), actorId: args.User.Id);

        try
        {
            await member.BanAsync();

            logger.LogInformation("{Member} ban completed by {User}", member, args.User);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BanAsync failed for {Member}, rolling back status to {Previous}",
                member, previousStatus);
            try
            {
                await entry.TransitionToAsync(db, previousStatus,
                    reason: $"rollback after failed ban by {args.User}", actorId: args.User.Id);
            }
            catch (Exception rollbackEx)
            {
                logger.LogError(rollbackEx,
                    "Rollback to {Previous} failed for {Member} after BanAsync failure (actor: {User})",
                    previousStatus, member, args.User);
            }

            throw;
        }

        await entry.RespondToInteraction(args, client);
    }

    private async Task HandleStrangerKick(ComponentInteractionCreateEventArgs args, DiscordClient client,
        GuildMember entry,
        DiscordMember member)
    {
        logger.LogInformation("{User} initiating kick of {Member} (current status {Status})",
            args.User, member, entry.Status);

        // Pre-mark in DB so that the GuildMemberRemoved event that fires immediately
        // after RemoveAsync sees the correct terminal status even if this process restarts.
        MemberStatus previousStatus = entry.Status;
        await entry.TransitionToAsync(db, MemberStatus.KickedByModerator,
            reason: args.User.ToString(), actorId: args.User.Id);

        try
        {
            await member.RemoveAsync();

            logger.LogInformation("{Member} kick completed by {User}", member, args.User);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RemoveAsync failed for {Member}, rolling back status to {Previous}",
                member, previousStatus);
            try
            {
                await entry.TransitionToAsync(db, previousStatus,
                    reason: $"rollback after failed kick by {args.User}", actorId: args.User.Id);
            }
            catch (Exception rollbackEx)
            {
                logger.LogError(rollbackEx,
                    "Rollback to {Previous} failed for {Member} after RemoveAsync failure (actor: {User})",
                    previousStatus, member, args.User);
            }

            throw;
        }

        await entry.RespondToInteraction(args, client);
    }

    private async Task HandleStrangerDisableAutoKick(
        ComponentInteractionCreateEventArgs args,
        DiscordClient client,
        GuildMember entry)
    {
        logger.LogInformation("Disabling auto-kick for {Member}", entry);

        entry.Application!.IsAutoKickEnabled = false;
        await db.SaveAsync(entry.Application);

        await db.SaveAsync(entry);

        await entry.RespondToInteraction(args, client);
    }

    /// <summary>
    ///     Builds an immediate interaction response that mirrors the current message with all buttons disabled,
    ///     providing instant visual feedback while the action runs in the background.
    /// </summary>
    private static DiscordInteractionResponseBuilder BuildBusyMirror(DiscordMessage message)
    {
        DiscordInteractionResponseBuilder builder = new();

        foreach (DiscordEmbed embed in message.Embeds)
        {
            builder.AddEmbed(embed);
        }

        foreach (DiscordActionRowComponent row in message.Components)
        {
            List<DiscordComponent> cloned = new();
            foreach (DiscordComponent component in row.Components)
            {
                if (component is DiscordButtonComponent button)
                {
                    cloned.Add(new DiscordButtonComponent(button).Disable());
                }
                else
                {
                    cloned.Add(component);
                }
            }

            builder.AddComponents(cloned);
        }

        return builder;
    }

    /// <summary>
    ///     Builds a webhook edit that restores the original message (with buttons re-enabled) so moderators
    ///     can retry after an error.
    /// </summary>
    private static DiscordWebhookBuilder BuildRestoredWidget(DiscordMessage message)
    {
        DiscordWebhookBuilder builder = new();

        foreach (DiscordEmbed embed in message.Embeds)
        {
            builder.AddEmbed(embed);
        }

        foreach (DiscordActionRowComponent row in message.Components)
        {
            List<DiscordComponent> cloned = new();
            foreach (DiscordComponent component in row.Components)
            {
                if (component is DiscordButtonComponent button)
                {
                    cloned.Add(new DiscordButtonComponent(button).Enable());
                }
                else
                {
                    cloned.Add(component);
                }
            }

            builder.AddComponents(cloned);
        }

        return builder;
    }
}