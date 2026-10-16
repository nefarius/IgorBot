namespace IgorBot.Services;

/// <summary>
///     Pure cursor and paging helpers for honeypot history reconciliation.
/// </summary>
internal static class HoneypotReconciliationEngine
{
    /// <summary>
    ///     How far back the first scan looks when no checkpoint exists yet.
    /// </summary>
    public static readonly TimeSpan InitialLookback = TimeSpan.FromHours(24);

    /// <summary>
    ///     Discord channel-history page size.
    /// </summary>
    public const int PageSize = 100;

    /// <summary>
    ///     Upper bound of pages fetched for one guild in a single tick.
    /// </summary>
    public const int MaxPagesPerGuild = 5;

    private const long DiscordEpochMilliseconds = 1_420_070_400_000L;

    /// <summary>
    ///     Builds a Discord snowflake whose timestamp equals <paramref name="utc" />.
    /// </summary>
    public static ulong SnowflakeFromTimestamp(DateTime utc)
    {
        DateTimeOffset dto = new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        long snowflake = (dto.ToUnixTimeMilliseconds() - DiscordEpochMilliseconds) << 22;
        return snowflake < 0 ? 0UL : (ulong)snowflake;
    }

    /// <summary>
    ///     Returns the message ID to pass to history fetches.
    ///     The 24-hour lookback applies only when no checkpoint exists.
    ///     An existing checkpoint is preserved unless it predates channel activation.
    /// </summary>
    public static ulong ResolveAfterId(
        ulong? lastProcessedMessageId,
        DateTime utcNow,
        DateTime? channelActivatedAt = null)
    {
        if (lastProcessedMessageId is ulong checkpoint)
        {
            if (channelActivatedAt is DateTime activated)
            {
                ulong activationId = SnowflakeFromTimestamp(activated);
                return checkpoint > activationId ? checkpoint : activationId;
            }

            return checkpoint;
        }

        DateTime lookbackStart = utcNow - InitialLookback;
        DateTime start = channelActivatedAt is DateTime firstActivation && firstActivation > lookbackStart
            ? firstActivation
            : lookbackStart;
        return SnowflakeFromTimestamp(start);
    }

    /// <summary>
    ///     A scan is connected when it reached the start of available history or
    ///     crossed the committed cursor, so there is no unevaluated gap.
    /// </summary>
    public static bool IsConnectedToCursor(ulong cursor, ulong? oldestFetchedId, bool reachedHistoryStart) =>
        reachedHistoryStart || oldestFetchedId is ulong oldest && oldest <= cursor;

    /// <summary>
    ///     When the scan has not reached the cursor, persist the oldest fetched ID
    ///     so the next tick can continue walking backward.
    /// </summary>
    public static ulong? ContinuationBeforeId(bool connected, ulong? oldestFetchedId) =>
        connected ? null : oldestFetchedId;

    /// <summary>
    ///     Advances the cursor through contiguous successful evaluations only.
    ///     <paramref name="evaluations" /> must be oldest-first.
    ///     A failed evaluation stops advancement so the next run retries that message.
    /// </summary>
    public static ulong? AdvanceCheckpoint(
        ulong? current,
        IEnumerable<(ulong MessageId, bool Success)> evaluations)
    {
        ulong? checkpoint = current;
        foreach ((ulong messageId, bool success) in evaluations)
        {
            if (!success)
            {
                break;
            }

            checkpoint = messageId;
        }

        return checkpoint;
    }

    /// <summary>
    ///     Discord returns history newest-first; reconciliation must evaluate oldest-first.
    /// </summary>
    public static IReadOnlyList<T> OrderOldestFirst<T>(IEnumerable<T> items, Func<T, ulong> idSelector) =>
        items.OrderBy(idSelector).ToList();

    /// <summary>
    ///     Oldest message ID eligible for backlog cleanup. Activation is exclusive, matching
    ///     forward scans. A missing activation leaves the whole checkpointed history eligible.
    /// </summary>
    public static ulong BacklogLowerBound(DateTime? channelActivatedAt) =>
        channelActivatedAt is DateTime activated ? SnowflakeFromTimestamp(activated) : 0UL;

    /// <summary>
    ///     A checkpointed message still needs deletion when it is newer than activation.
    ///     The committed checkpoint message itself is included.
    /// </summary>
    public static bool IsBacklogMessage(ulong messageId, ulong checkpoint, ulong lowerBound) =>
        messageId > lowerBound && messageId <= checkpoint;

    /// <summary>
    ///     Exclusive Discord <c>before</c> ID that still returns <paramref name="messageId" />.
    /// </summary>
    public static ulong BeforeIdIncluding(ulong messageId) =>
        messageId == ulong.MaxValue ? messageId : messageId + 1UL;

    /// <summary>
    ///     Checkpointed message IDs selected for backlog cleanup, newest first.
    /// </summary>
    public static IReadOnlyList<ulong> SelectBacklogMessageIds(
        IEnumerable<ulong> messageIds,
        ulong checkpoint,
        ulong lowerBound) =>
        messageIds.Where(id => IsBacklogMessage(id, checkpoint, lowerBound))
            .OrderByDescending(id => id)
            .ToList();
}
