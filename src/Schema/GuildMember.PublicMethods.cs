using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

using MongoDB.Entities;

using Serilog;

namespace IgorBot.Schema;

internal sealed partial class GuildMember
{
    /// <summary>
    ///     Transitions this member to a new <see cref="MemberStatus" />, recording a history event,
    ///     mirroring to the legacy timestamp fields for backwards compatibility, and persisting.
    ///     The canonical-state fields and the history-array append are issued as a single atomic
    ///     MongoDB <c>updateOne</c> ($set + $push) so no intermediate state is visible to other
    ///     readers and a concurrent append to StatusHistory cannot be lost by a full-document
    ///     replace racing with this call.
    /// </summary>
    public async Task TransitionToAsync(
        DB db,
        MemberStatus next,
        string? reason = null,
        ulong? actorId = null)
    {
        if (Status == next)
        {
            return;
        }

        Log.Information(
            "GuildMember {MemberId} transitioning {From} -> {To} reason={Reason} actor={ActorId}",
            ID, Status, next, reason, actorId);

        DateTime now = DateTime.UtcNow;

        MemberStatusEvent evt = new()
        {
            From = Status, To = next, At = now, Reason = reason, ActorId = actorId
        };

        // Build an atomic updateOne: $set the canonical fields + $push the history event.
        // All legacy timestamp/flag fields are cleared first so that rollbacks (e.g.
        // BannedByModerator → Onboarding) never leave stale values like BannedAt or
        // RemovedByModeration=true in the document. Each switch branch then re-sets only
        // the fields that belong to the target status.
        Update<GuildMember> update = db.Update<GuildMember>()
            .MatchID(ID)
            .Modify(m => m.Status, next)
            .Modify(m => m.StatusChangedAt, (DateTime?)now)
            .Modify(m => m.StatusReason, reason)
            .Modify(b => b.Push(m => m.StatusHistory, evt))
            // Clear all legacy terminal fields unconditionally.
            .Modify(m => m.LeftAt, (DateTime?)null)
            .Modify(m => m.KickedAt, (DateTime?)null)
            .Modify(m => m.BannedAt, (DateTime?)null)
            .Modify(m => m.AutoKickedAt, (DateTime?)null)
            .Modify(m => m.PromotedAt, (DateTime?)null)
            .Modify(m => m.FullMemberAt, (DateTime?)null)
            .Modify(m => m.StrangerRoleRemovedAt, (DateTime?)null)
            .Modify(m => m.RemovedByModeration, false);

        // Re-set only the fields that apply to the target status.
        switch (next)
        {
            case MemberStatus.LeftVoluntarily:
                update = update.Modify(m => m.LeftAt, (DateTime?)now);
                break;
            case MemberStatus.KickedByModerator:
            case MemberStatus.KickedExternally:
                update = update
                    .Modify(m => m.KickedAt, (DateTime?)now)
                    .Modify(m => m.RemovedByModeration, true);
                break;
            case MemberStatus.BannedByModerator:
            case MemberStatus.BannedByHoneypot:
            case MemberStatus.BannedExternally:
                update = update
                    .Modify(m => m.BannedAt, (DateTime?)now)
                    .Modify(m => m.RemovedByModeration, true);
                break;
            case MemberStatus.AutoKicked:
                update = update.Modify(m => m.AutoKickedAt, (DateTime?)now);
                break;
            case MemberStatus.FullMember:
                update = update
                    .Modify(m => m.PromotedAt, (DateTime?)now)
                    .Modify(m => m.FullMemberAt, (DateTime?)now);
                break;
            case MemberStatus.StrangerRoleRemoved:
                update = update.Modify(m => m.StrangerRoleRemovedAt, (DateTime?)now);
                break;
        }

        await update.ExecuteAsync();

        Log.Information(
            "GuildMember {MemberId} transitioned {From} -> {To}",
            ID, Status, next);

        // Mirror to in-memory state only after the DB write succeeded so the
        // in-memory object never gets ahead of what is persisted.
        // Clear all legacy fields first, matching the DB update above.
        LeftAt = null;
        KickedAt = null;
        BannedAt = null;
        AutoKickedAt = null;
        PromotedAt = null;
        FullMemberAt = null;
        StrangerRoleRemovedAt = null;
        RemovedByModeration = false;

        StatusHistory.Add(evt);
        Status = next;
        StatusChangedAt = now;
        StatusReason = reason;

        switch (next)
        {
            case MemberStatus.LeftVoluntarily:
                LeftAt = now;
                break;
            case MemberStatus.KickedByModerator:
            case MemberStatus.KickedExternally:
                KickedAt = now;
                RemovedByModeration = true;
                break;
            case MemberStatus.BannedByModerator:
            case MemberStatus.BannedByHoneypot:
            case MemberStatus.BannedExternally:
                BannedAt = now;
                RemovedByModeration = true;
                break;
            case MemberStatus.AutoKicked:
                AutoKickedAt = now;
                break;
            case MemberStatus.FullMember:
                PromotedAt = now;
                FullMemberAt = now;
                break;
            case MemberStatus.StrangerRoleRemoved:
                StrangerRoleRemovedAt = now;
                break;
        }
    }

    /// <summary>
    ///     Resets certain properties to their defaults. Call when a member (re-)joined.
    ///     Appends a rejoin event to <see cref="StatusHistory" /> in memory; the caller is
    ///     responsible for persisting via <c>db.SaveAsync</c>.
    /// </summary>
    public void Reset()
    {
        const string rejoinReason = "rejoin";
        DateTime now = DateTime.UtcNow;

        Log.Information(
            "GuildMember {MemberId} Reset from {PreviousStatus} (rejoin)",
            ID, Status);

        // Record the rejoin before wiping the lifecycle fields so the history
        // shows what state the member was in when they came back.
        StatusHistory.Add(new MemberStatusEvent
        {
            From = Status,
            To = MemberStatus.New,
            At = now,
            Reason = rejoinReason
        });

        LeftAt = null;
        KickedAt = null;
        BannedAt = null;
        AutoKickedAt = null;
        PromotedAt = null;
        FullMemberAt = null;
        StrangerRoleRemovedAt = null;
        RemovedByModeration = false;

        // Canonical fields use the same timestamp and reason as the history entry.
        Status = MemberStatus.New;
        StatusChangedAt = now;
        StatusReason = rejoinReason;

        Application = null;
        Channel = null;
    }

    /// <summary>
    ///     Deletes the application widget form the database.
    /// </summary>
    public async Task DeleteApplication(DB db)
    {
        if (Application is null)
        {
            return;
        }

        await db.DeleteAsync(Application);

        Application = null;
        await db.SaveAsync(this);
    }

    /// <summary>
    ///     Deletes the newbie channel form the database.
    /// </summary>
    public async Task DeleteChannel(DB db)
    {
        if (Channel is null)
        {
            return;
        }

        await db.DeleteAsync(Channel);
        Channel = null;
        await db.SaveAsync(this);
    }


    /// <summary>
    ///     Create a newbie channel in the database.
    /// </summary>
    public async Task CreateNewbieChannel(DB db, DiscordGuild guild, DiscordChannel channel)
    {
        NewbieChannel newbieChannel =
            new() { GuildId = guild.Id, ChannelId = channel.Id, ChannelName = channel.Name, Mention = channel.Mention };
        await db.SaveAsync(newbieChannel);

        Channel = newbieChannel;
        await db.SaveAsync(this);
    }

    /// <summary>
    ///     Creates the Discord message widget and application a new application.
    /// </summary>
    public async Task CreateApplicationWidget(
        DB db,
        DiscordClient client,
        DiscordGuild guild,
        DiscordChannel strangerStatusChannel
    )
    {
        DiscordEmbedBuilder initialEmbed = GetStatusWidget(client);

        // Create message so we have a unique ID
        DiscordMessage statusMsg = await strangerStatusChannel.SendMessageAsync(initialEmbed);

        // Create application entity
        StrangerApplicationEmbed application = new()
        {
            GuildId = guild.Id, ChannelId = strangerStatusChannel.Id, MessageId = statusMsg.Id
        };

        // generates entity ID
        await db.SaveAsync(application);

        DiscordMessageBuilder messageBuilder = new DiscordMessageBuilder().AddEmbed(initialEmbed);

        if (!HasLeftGuild)
        {
            messageBuilder.AddComponents(ButtonComponents);
        }

        if (!HasLeftGuild && application.ButtonComponents.Any())
        {
            messageBuilder.AddComponents(application.ButtonComponents);
        }

        // Add action buttons to message
        await statusMsg.ModifyAsync(messageBuilder);

        // Store in DB
        Application = application;
        await db.SaveAsync(this);
    }

    /// <summary>
    ///     Updates the Discord message according to the state of this object.
    /// </summary>
    public async Task UpdateApplicationWidget(DiscordClient client, bool isDeleted = false)
    {
        if (Application is null)
        {
            throw new ArgumentNullException(nameof(Application), "Application is null, can't identify status widget.");
        }

        DiscordEmbedBuilder statusEmbed = GetStatusWidget(client);

        DiscordMessageBuilder status = new();

        status.AddEmbed(statusEmbed);

        if (!HasLeftGuild && ButtonComponents.Any())
        {
            status.AddComponents(ButtonComponents);
        }

        if (!isDeleted)
        {
            if (Application.ButtonComponents.Any())
            {
                status.AddComponents(Application.ButtonComponents);
            }
        }

        DiscordGuild guild = client.Guilds[GuildId];

        DiscordMessage statusMessage = await guild
            .GetChannel(Application.ChannelId)
            .GetMessageAsync(Application.MessageId);

        await statusMessage.ModifyAsync(status);
    }

    /// <summary>
    ///     Respond to an interaction with an updated status widget.
    /// </summary>
    public async Task RespondToInteraction(ComponentInteractionCreateEventArgs args, DiscordClient client)
    {
        await args.Interaction.EditOriginalResponseAsync(GetInteractionResponse(client));
    }

    /// <summary>
    ///     Updates the application widget with the last status and removes the application association with this member from
    ///     the DB.
    /// </summary>
    public async Task DeleteApplicationWidget(DB db, DiscordClient client)
    {
        await UpdateApplicationWidget(client, true);

        await db.DeleteAsync(Application!);
        Application = null;
        await db.SaveAsync(this);
    }
}