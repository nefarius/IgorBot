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
    ///     Returns the message ID to pass to <c>GetMessagesAfterAsync</c>.
    ///     Missing checkpoints start at <see cref="InitialLookback" /> before <paramref name="utcNow" />.
    /// </summary>
    public static ulong ResolveAfterId(ulong? lastProcessedMessageId, DateTime utcNow) =>
        lastProcessedMessageId ?? SnowflakeFromTimestamp(utcNow - InitialLookback);

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
}
