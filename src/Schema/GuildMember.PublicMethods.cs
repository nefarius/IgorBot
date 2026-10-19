using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using DSharpPlus.EventArgs;

using MongoDB.Driver;
using MongoDB.Entities;

using IgorBot.Util;

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
        await TransitionCoreAsync(db, next, reason, actorId, null);
    }

    /// <summary>
    ///     Compare-and-set variant of <see cref="TransitionToAsync" />: the write only applies when the persisted
    ///     status is one of <paramref name="allowedFrom" /> (defaults to <see cref="MemberTransitionPolicy" />).
    ///     Returns <see langword="false" /> and leaves the in-memory object untouched when another writer got there
    ///     first; the caller decides whether to reload or give up.
    /// </summary>
    public Task<bool> TryTransitionAsync(
        DB db,
        MemberStatus next,
        IReadOnlyCollection<MemberStatus>? allowedFrom = null,
        string? reason = null,
        ulong? actorId = null,
        Func<FilterDefinitionBuilder<GuildMember>, FilterDefinition<GuildMember>>? extraMatch = null)
    {
        allowedFrom ??= MemberTransitionPolicy.AllowedFrom(next);
        return TransitionCoreAsync(db, next, reason, actorId, allowedFrom, extraMatch);
    }

    private async Task<bool> TransitionCoreAsync(
        DB db,
        MemberStatus next,
        string? reason,
        ulong? actorId,
        IReadOnlyCollection<MemberStatus>? allowedFrom,
        Func<FilterDefinitionBuilder<GuildMember>, FilterDefinition<GuildMember>>? extraMatch = null)
    {
        if (Status == next)
        {
            return true;
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

        if (allowedFrom is not null)
        {
            MemberStatus[] allowed = allowedFrom.ToArray();
            bool includesUnknown = allowed.Contains(MemberStatus.Unknown);
            update = update.Match(f => includesUnknown
                ? f.In(m => m.Status, allowed) | f.Exists(m => m.Status, false)
                : f.In(m => m.Status, allowed));
        }

        if (extraMatch is not null)
        {
            update = update.Match(extraMatch);
        }

        UpdateResult result = await update.ExecuteAsync();

        if ((allowedFrom is not null || extraMatch is not null) && result.MatchedCount == 0)
        {
            Log.Warning(
                "GuildMember {MemberId} transition {From} -> {To} rejected: persisted status is not in the allowed set",
                ID, Status, next);
            return false;
        }

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

        return true;
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

        StrangerApplicationEmbed application = Application;
        await ClearApplicationReferenceAsync(db, application);
        await db.DeleteAsync(application);
        Application = null;
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

        NewbieChannel channel = Channel;

        // Targeted update matched on the channel ID: a newer channel assigned meanwhile is never wiped.
        await db.Update<GuildMember>()
            .MatchID(ID)
            .Match(f => f.Eq(m => m.Channel!.ID, channel.ID))
            .Modify(m => m.Channel, (NewbieChannel?)null)
            .ExecuteAsync();

        await db.DeleteAsync(channel);
        Channel = null;
    }

    /// <summary>
    ///     Clears the embedded application reference, but only while it still points at
    ///     <paramref name="application" />.
    /// </summary>
    private async Task ClearApplicationReferenceAsync(DB db, StrangerApplicationEmbed application)
    {
        await db.Update<GuildMember>()
            .MatchID(ID)
            .Match(f => f.Eq(m => m.Application!.ID, application.ID))
            .Modify(m => m.Application, (StrangerApplicationEmbed?)null)
            .ExecuteAsync();
    }

    /// <summary>
    ///     Persists the auto-kick flag on the embedded application (the authoritative copy) without touching
    ///     anything else on the member document.
    /// </summary>
    public async Task SetAutoKickEnabledAsync(DB db, bool enabled)
    {
        if (Application is null)
        {
            return;
        }

        await db.Update<GuildMember>()
            .MatchID(ID)
            .Match(f => f.Eq(m => m.Application!.ID, Application.ID))
            .Modify(m => m.Application!.IsAutoKickEnabled, enabled)
            .ExecuteAsync();
        Application.IsAutoKickEnabled = enabled;
    }

    /// <summary>
    ///     Persists the questionnaire submission timestamp and optional screening decision on the embedded
    ///     application.
    /// </summary>
    public async Task SetQuestionnaireSubmittedAsync(DB db, DateTime submittedAt, OnboardingDecisionRecord? decision)
    {
        if (Application is null)
        {
            return;
        }

        Update<GuildMember> update = db.Update<GuildMember>()
            .MatchID(ID)
            .Match(f => f.Eq(m => m.Application!.ID, Application.ID))
            .Modify(m => m.Application!.QuestionnaireSubmittedAt, (DateTime?)submittedAt);

        if (decision is not null)
        {
            update = update.Modify(m => m.Application!.Decision, decision);
        }

        await update.ExecuteAsync();
        Application.QuestionnaireSubmittedAt = submittedAt;
        if (decision is not null)
        {
            Application.Decision = decision;
        }
    }

    /// <summary>
    ///     Persists the screening decision on the embedded application.
    /// </summary>
    public async Task SetApplicationDecisionAsync(DB db, OnboardingDecisionRecord decision)
    {
        if (Application is null)
        {
            return;
        }

        await db.Update<GuildMember>()
            .MatchID(ID)
            .Match(f => f.Eq(m => m.Application!.ID, Application.ID))
            .Modify(m => m.Application!.Decision, decision)
            .ExecuteAsync();
        Application.Decision = decision;
    }

    /// <summary>
    ///     Stamps <see cref="LeftAt" /> for legacy queries without touching the canonical status.
    /// </summary>
    public async Task StampLeftAtAsync(DB db)
    {
        DateTime now = DateTime.UtcNow;
        await db.Update<GuildMember>()
            .MatchID(ID)
            .Modify(m => m.LeftAt, (DateTime?)now)
            .ExecuteAsync();
        LeftAt = now;
    }

    /// <summary>
    ///     Persists the in-progress onboarding flag with a targeted update.
    /// </summary>
    public async Task SetOnboardingInProgressAsync(DB db, bool value)
    {
        await db.Update<GuildMember>()
            .MatchID(ID)
            .Modify(m => m.IsOnboardingInProgress, value)
            .ExecuteAsync();
        IsOnboardingInProgress = value;
    }

    /// <summary>
    ///     Updates the cached display strings with a targeted update.
    /// </summary>
    public async Task UpdateDisplayAsync(DB db, string member, string mention)
    {
        await db.Update<GuildMember>()
            .MatchID(ID)
            .Modify(m => m.Member, member)
            .Modify(m => m.Mention, mention)
            .ExecuteAsync();
        Member = member;
        Mention = mention;
    }

    /// <summary>
    ///     Atomically creates the member document if it does not exist (<c>$setOnInsert</c> upsert) and returns
    ///     the persisted document. Safe against concurrent creators: exactly one of them reports
    ///     <c>created == true</c>, and nobody overwrites existing data.
    /// </summary>
    public static async Task<(GuildMember Member, bool Created)> EnsureExistsAsync(
        DB db,
        ulong guildId,
        ulong memberId,
        string display,
        string mention,
        MemberStatus initialStatus,
        string? initialReason,
        MemberStatusEvent? initialEvent = null)
    {
        string id = $"{guildId}-{memberId}";
        DateTime now = DateTime.UtcNow;
        List<MemberStatusEvent> history = initialEvent is null ? [] : [initialEvent];

        FilterDefinition<GuildMember> filter = Builders<GuildMember>.Filter.Eq(m => m.ID, id);
        UpdateDefinition<GuildMember> upsert = Builders<GuildMember>.Update
            .SetOnInsert(m => m.GuildId, guildId)
            .SetOnInsert(m => m.MemberId, memberId)
            .SetOnInsert(m => m.Member, display)
            .SetOnInsert(m => m.Mention, mention)
            .SetOnInsert(m => m.JoinedAt, now)
            .SetOnInsert(m => m.Status, initialStatus)
            .SetOnInsert(m => m.StatusChangedAt, initialStatus == MemberStatus.Unknown ? null : (DateTime?)now)
            .SetOnInsert(m => m.StatusReason, initialReason)
            .SetOnInsert(m => m.StatusHistory, history)
            .SetOnInsert(m => m.RemovedByModeration, false)
            .SetOnInsert(m => m.IsOnboardingInProgress, false);

        GuildMember? before = null;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                before = await db.Collection<GuildMember>().FindOneAndUpdateAsync(
                    filter,
                    upsert,
                    new FindOneAndUpdateOptions<GuildMember>
                    {
                        IsUpsert = true, ReturnDocument = ReturnDocument.Before
                    });
                break;
            }
            catch (MongoCommandException ex) when (attempt == 0 && ex.Code == 11000)
            {
                // Lost the upsert race to another creator; the retry matches their document.
            }
            catch (MongoWriteException ex) when (attempt == 0 && ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
            }
        }

        GuildMember? current = await db.Find<GuildMember>().OneAsync(id);
        return (current ?? throw new InvalidOperationException($"Member {id} vanished after upsert."),
            before is null);
    }

    /// <summary>
    ///     Atomically re-activates a returning member: one <c>$set</c> + <c>$push</c> that clears lifecycle fields,
    ///     detaches the old channel/widget and records a rejoin event. Skipped (returns <see langword="false" />)
    ///     when a honeypot ban was recorded moments ago, so a slow join handler cannot undo a ban that raced it.
    /// </summary>
    public async Task<bool> ReactivateAsync(DB db)
    {
        DateTime now = DateTime.UtcNow;
        DateTime banCutoff = now - MemberTransitionPolicy.ExternalKickUpgradeWindow;

        MemberStatusEvent evt = new()
        {
            From = Status, To = MemberStatus.New, At = now, Reason = "rejoin"
        };

        UpdateResult result = await db.Update<GuildMember>()
            .MatchID(ID)
            .Match(f => !(f.Eq(m => m.Status, MemberStatus.BannedByHoneypot) &
                          f.Gt(m => m.StatusChangedAt, (DateTime?)banCutoff)))
            .Modify(m => m.JoinedAt, now)
            .Modify(m => m.Status, MemberStatus.New)
            .Modify(m => m.StatusChangedAt, (DateTime?)now)
            .Modify(m => m.StatusReason, "rejoin")
            .Modify(b => b.Push(m => m.StatusHistory, evt))
            .Modify(m => m.LeftAt, (DateTime?)null)
            .Modify(m => m.KickedAt, (DateTime?)null)
            .Modify(m => m.BannedAt, (DateTime?)null)
            .Modify(m => m.AutoKickedAt, (DateTime?)null)
            .Modify(m => m.PromotedAt, (DateTime?)null)
            .Modify(m => m.FullMemberAt, (DateTime?)null)
            .Modify(m => m.StrangerRoleRemovedAt, (DateTime?)null)
            .Modify(m => m.RemovedByModeration, false)
            .Modify(m => m.Application, (StrangerApplicationEmbed?)null)
            .Modify(m => m.Channel, (NewbieChannel?)null)
            .ExecuteAsync();

        if (result.MatchedCount == 0)
        {
            Log.Warning("GuildMember {MemberId} rejoin reset skipped: fresh honeypot ban recorded", ID);
            return false;
        }

        Reset();
        JoinedAt = now;
        return true;
    }


    /// <summary>
    ///     Create a newbie channel in the database.
    /// </summary>
    public async Task CreateNewbieChannel(DB db, DiscordGuild guild, DiscordChannel channel)
    {
        NewbieChannel newbieChannel =
            new() { GuildId = guild.Id, ChannelId = channel.Id, ChannelName = channel.Name, Mention = channel.Mention };
        await db.SaveAsync(newbieChannel);

        // Targeted update: don't overwrite concurrent changes to the rest of the member document.
        await db.Update<GuildMember>().MatchID(ID).Modify(m => m.Channel, newbieChannel).ExecuteAsync();
        Channel = newbieChannel;
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
        StrangerApplicationEmbed? application = null;

        try
        {
            // Create application entity
            application = new StrangerApplicationEmbed
            {
                GuildId = guild.Id, ChannelId = strangerStatusChannel.Id, MessageId = statusMsg.Id
            };

            // generates entity ID
            await db.SaveAsync(application);

            DiscordMessageBuilder messageBuilder = new DiscordMessageBuilder().AddEmbed(initialEmbed);

            if (!HasLeftGuild)
            {
                messageBuilder.AddActionRowComponent(ButtonComponents);
            }

            if (!HasLeftGuild && application.ButtonComponents.Any())
            {
                messageBuilder.AddActionRowComponent(application.ButtonComponents);
            }

            // Add action buttons to message
            await statusMsg.ModifyAsync(messageBuilder);

            // Store in DB
            await db.Update<GuildMember>().MatchID(ID).Modify(m => m.Application, application).ExecuteAsync();
            Application = application;
        }
        catch
        {
            // Don't leave a half-created widget behind (message and/or application record).
            Application = null;
            try
            {
                await statusMsg.DeleteAsync();
            }
            catch (Exception)
            {
                // best effort
            }

            if (application is not null && !application.HasDefaultID())
            {
                try
                {
                    await db.DeleteAsync(application);
                }
                catch (Exception)
                {
                    // best effort
                }
            }

            throw;
        }
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
            status.AddActionRowComponent(ButtonComponents);
        }

        if (!isDeleted)
        {
            if (Application.ButtonComponents.Any())
            {
                status.AddActionRowComponent(Application.ButtonComponents);
            }
        }

        DiscordGuild guild = client.Guilds[GuildId];

        DiscordChannel channel = guild.FindChannel(Application.ChannelId)
                                 ?? throw new InvalidOperationException(
                                     $"Application channel {Application.ChannelId} is not in the guild cache.");
        DiscordMessage statusMessage = await channel.GetMessageAsync(Application.MessageId);

        await statusMessage.ModifyAsync(status);
    }

    /// <summary>
    ///     Respond to an interaction with an updated status widget.
    /// </summary>
    public async Task RespondToInteraction(ComponentInteractionCreatedEventArgs args, DiscordClient client)
    {
        await args.Interaction.EditOriginalResponseAsync(GetInteractionResponse(client));
    }

    /// <summary>
    ///     Updates the application widget with the last status and removes the application association with this member from
    ///     the DB.
    /// </summary>
    public async Task DeleteApplicationWidget(DB db, DiscordClient client)
    {
        if (Application is null)
        {
            return;
        }

        try
        {
            await UpdateApplicationWidget(client, true);
        }
        catch (NotFoundException)
        {
            // Message already gone; the DB reference must still be cleared.
            Log.Warning("Application widget message for {MemberId} no longer exists", ID);
        }

        StrangerApplicationEmbed application = Application;
        await ClearApplicationReferenceAsync(db, application);
        await db.DeleteAsync(application);
        Application = null;
    }

    /// <summary>
    ///     Removes the application widget entirely: deletes the Discord message and the application record.
    ///     Unlike <see cref="DeleteApplicationWidget" />, the message is not kept as a final-status record.
    /// </summary>
    public async Task RemoveApplicationWidget(DB db, DiscordClient client)
    {
        if (Application is null)
        {
            return;
        }

        try
        {
            DiscordChannel? channel = client.Guilds[GuildId].FindChannel(Application.ChannelId);
            if (channel is not null)
            {
                DiscordMessage message = await channel.GetMessageAsync(Application.MessageId);
                await message.DeleteAsync();
            }
        }
        catch (NotFoundException)
        {
            // message already gone
        }

        StrangerApplicationEmbed application = Application;
        await ClearApplicationReferenceAsync(db, application);
        await db.DeleteAsync(application);
        Application = null;
    }
}
