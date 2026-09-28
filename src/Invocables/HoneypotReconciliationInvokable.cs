using System.Diagnostics.CodeAnalysis;

using Coravel.Invocable;

using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;

using MongoDB.Entities;

using Nefarius.DSharpPlus.Extensions.Hosting;

namespace IgorBot.Invocables;

/// <summary>
///     Periodic scan of honeypot channels for messages missed while the Discord gateway was down.
/// </summary>
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal sealed class HoneypotReconciliationInvokable(
    DB db,
    IGuildConfigService guildConfigService,
    IDiscordClientService discord,
    IDiscordReadinessService readiness,
    IHoneypotEnforcementService enforcement,
    ILogger<HoneypotReconciliationInvokable> logger) : IInvocable
{
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    /// <summary>
    ///     Minimum delay between Discord history fetches to stay under per-channel rate limits.
    /// </summary>
    private static readonly TimeSpan DiscordApiThrottleDelay = TimeSpan.FromSeconds(1.2);

    public async Task Invoke()
    {
        if (!await RunLock.WaitAsync(TimeSpan.Zero))
        {
            logger.LogDebug("Honeypot reconciliation already running, skipping tick");
            return;
        }

        try
        {
            await InvokeCore();
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task InvokeCore()
    {
        logger.LogDebug("Running honeypot reconciliation");

        IReadOnlyList<GuildConfig> configs = await guildConfigService.GetAllAsync();

        foreach (GuildConfig config in configs.Where(c => c.HoneypotChannelId.HasValue))
        {
            try
            {
                await ReconcileGuildAsync(config);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Honeypot reconciliation failed for guild {GuildId}; continuing with remaining guilds",
                    config.GuildId);
            }
        }
    }

    private async Task ReconcileGuildAsync(GuildConfig config)
    {
        ulong guildId = config.GuildId;
        ulong channelId = config.HoneypotChannelId!.Value;

        if (!discord.Client.Guilds.TryGetValue(guildId, out DiscordGuild? guild))
        {
            if (readiness.IsGuildReady(guildId))
            {
                logger.LogWarning(
                    "Guild {GuildId} not present in client, skipping honeypot reconciliation",
                    guildId);
            }
            else
            {
                logger.LogDebug(
                    "Guild {GuildId} not yet available in client (startup), skipping honeypot reconciliation",
                    guildId);
            }

            return;
        }

        DiscordChannel? channel = guild.GetChannel(channelId);
        if (channel is null)
        {
            logger.LogWarning(
                "Honeypot channel {ChannelId} not found in guild {GuildId}, skipping reconciliation",
                channelId, guildId);
            return;
        }

        HoneypotReconciliationState? state = await db.Find<HoneypotReconciliationState>()
            .OneAsync($"{guildId}-{channelId}");

        ulong cursor = HoneypotReconciliationEngine.ResolveAfterId(
            state?.LastProcessedMessageId, DateTime.UtcNow, config.HoneypotChannelActivatedAt);

        HistoryPageResult history = await FetchHistoryAsync(channel, guildId, channelId, cursor, state);
        if (history.FetchFailed)
        {
            return;
        }

        if (history.Messages.Count == 0)
        {
            await SaveStateAsync(state, guildId, channelId, cursor, continuationBefore: null);
            return;
        }

        bool connected = HoneypotReconciliationEngine.IsConnectedToCursor(
            cursor, history.OldestFetchedId, history.ReachedHistoryStart);
        ulong? continuation = HoneypotReconciliationEngine.ContinuationBeforeId(
            connected, history.OldestFetchedId);

        IReadOnlyList<DiscordMessage> ordered = HoneypotReconciliationEngine.OrderOldestFirst(
            history.Messages.Where(m => m.Id > cursor), static m => m.Id);

        if (!connected)
        {
            foreach (DiscordMessage message in ordered)
            {
                await EvaluateMessageAsync(guild, config, message);
            }

            ulong committed = state?.LastProcessedMessageId ?? cursor;
            await SaveStateAsync(state, guildId, channelId, committed, continuation);
            return;
        }

        List<(ulong MessageId, bool Success)> evaluations = new(ordered.Count);
        foreach (DiscordMessage message in ordered)
        {
            bool success = await EvaluateMessageAsync(guild, config, message);
            evaluations.Add((message.Id, success));
            if (!success)
            {
                break;
            }
        }

        ulong? next = HoneypotReconciliationEngine.AdvanceCheckpoint(cursor, evaluations);
        await SaveStateAsync(state, guildId, channelId, next ?? cursor, continuationBefore: null);
    }

    private async Task<HistoryPageResult> FetchHistoryAsync(
        DiscordChannel channel,
        ulong guildId,
        ulong channelId,
        ulong cursor,
        HoneypotReconciliationState? state)
    {
        List<DiscordMessage> collected = [];
        bool reachedHistoryStart = false;
        int pagesFetched = 0;

        try
        {
            if (state?.ContinuationBeforeMessageId is ulong continueBefore)
            {
                reachedHistoryStart = await WalkBackwardAsync(
                    channel, continueBefore, cursor, collected, pagesFetched);
            }
            else
            {
                IReadOnlyList<DiscordMessage> afterPage =
                    await channel.GetMessagesAfterAsync(cursor, HoneypotReconciliationEngine.PageSize);
                pagesFetched++;

                if (afterPage.Count == 0)
                {
                    return HistoryPageResult.EmptyConnected;
                }

                collected.AddRange(afterPage);
                if (afterPage.Count < HoneypotReconciliationEngine.PageSize)
                {
                    reachedHistoryStart = true;
                }
                else
                {
                    ulong oldest = afterPage.Min(m => m.Id);
                    reachedHistoryStart = await WalkBackwardAsync(
                        channel, oldest, cursor, collected, pagesFetched);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to fetch honeypot history for guild {GuildId} channel {ChannelId} after {AfterId}",
                guildId, channelId, cursor);
            return HistoryPageResult.Failed;
        }

        return new HistoryPageResult(collected, reachedHistoryStart, false);
    }

    private async Task<bool> WalkBackwardAsync(
        DiscordChannel channel,
        ulong beforeId,
        ulong cursor,
        List<DiscordMessage> collected,
        int pagesAlreadyFetched)
    {
        ulong walkFrom = beforeId;
        for (int page = pagesAlreadyFetched; page < HoneypotReconciliationEngine.MaxPagesPerGuild; page++)
        {
            if (page > 0 || collected.Count > 0)
            {
                await Task.Delay(DiscordApiThrottleDelay);
            }

            IReadOnlyList<DiscordMessage> older =
                await channel.GetMessagesBeforeAsync(walkFrom, HoneypotReconciliationEngine.PageSize);

            if (older.Count == 0)
            {
                return true;
            }

            collected.AddRange(older);
            walkFrom = older.Min(m => m.Id);

            if (older.Count < HoneypotReconciliationEngine.PageSize || walkFrom <= cursor)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> EvaluateMessageAsync(
        DiscordGuild guild,
        GuildConfig config,
        DiscordMessage message)
    {
        if (message.Author is null || message.Author.IsBot)
        {
            return true;
        }

        bool isOwner = false;
        IReadOnlyList<ulong> roleIds = [];

        try
        {
            DiscordMember member = await guild.GetMemberAsync(message.Author.Id);
            isOwner = member.IsOwner;
            roleIds = member.Roles.Select(r => r.Id).ToList();
        }
        catch (NotFoundException)
        {
            logger.LogDebug(
                "Honeypot reconciliation: author {AuthorId} is no longer in guild {GuildId}",
                message.Author.Id, guild.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to resolve honeypot author {AuthorId} in guild {GuildId}; will retry message {MessageId}",
                message.Author.Id, guild.Id, message.Id);
            return false;
        }

        HoneypotEligibility eligibility = HoneypotRules.Evaluate(
            message.Author.IsBot,
            isOwner,
            roleIds,
            config.HoneypotExclusionRoleIds);

        switch (eligibility)
        {
            case HoneypotEligibility.SkipOwner:
                return true;
            case HoneypotEligibility.SkipExcludedRole:
                logger.LogWarning(
                    "Member {Member} posted in honeypot channel but has excluded role",
                    message.Author);
                return true;
            case HoneypotEligibility.SkipBot:
                return true;
            case HoneypotEligibility.Enforce:
                break;
            default:
                return true;
        }

        try
        {
            HoneypotEnforcementOutcome outcome = await enforcement.EnforceAsync(
                guild.Id,
                message.Author.Id,
                message.Author.ToString(),
                message.Author.Mention);

            if (outcome == HoneypotEnforcementOutcome.PermanentFailure)
            {
                logger.LogError(
                    "Permanent honeypot enforcement failure for {Author} on message {MessageId}; advancing checkpoint",
                    message.Author, message.Id);
                return true;
            }

            if (outcome == HoneypotEnforcementOutcome.Failed)
            {
                logger.LogWarning(
                    "Honeypot enforcement failed for {Author} on message {MessageId}; checkpoint will not advance past it",
                    message.Author, message.Id);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unexpected honeypot enforcement error for {Author} on message {MessageId}",
                message.Author, message.Id);
            return false;
        }
    }

    private async Task SaveStateAsync(
        HoneypotReconciliationState? existing,
        ulong guildId,
        ulong channelId,
        ulong lastProcessedMessageId,
        ulong? continuationBefore)
    {
        HoneypotReconciliationState state = existing ?? new HoneypotReconciliationState
        {
            GuildId = guildId,
            ChannelId = channelId
        };

        if (existing is not null
            && state.LastProcessedMessageId == lastProcessedMessageId
            && state.ContinuationBeforeMessageId == continuationBefore)
        {
            return;
        }

        state.GuildId = guildId;
        state.ChannelId = channelId;
        state.LastProcessedMessageId = lastProcessedMessageId;
        state.ContinuationBeforeMessageId = continuationBefore;
        state.LastReconciledAt = DateTime.UtcNow;
        state.ID = state.GenerateNewID();
        await db.SaveAsync(state);

        logger.LogDebug(
            "Honeypot reconciliation checkpoint for guild {GuildId} channel {ChannelId} is now {MessageId} (continuation {Continuation})",
            guildId, channelId, lastProcessedMessageId, continuationBefore);
    }

    private readonly record struct HistoryPageResult(
        IReadOnlyList<DiscordMessage> Messages,
        bool ReachedHistoryStart,
        bool FetchFailed)
    {
        public ulong? OldestFetchedId =>
            Messages.Count == 0 ? null : Messages.Min(static m => m.Id);

        public static HistoryPageResult EmptyConnected { get; } = new([], true, false);

        public static HistoryPageResult Failed { get; } = new([], false, true);
    }
}
