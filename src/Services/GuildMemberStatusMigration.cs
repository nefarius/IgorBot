using IgorBot.Schema;

using MongoDB.Entities;

using Serilog;

namespace IgorBot.Services;

/// <summary>
///     One-time, idempotent migration that stamps a canonical <see cref="MemberStatus" /> and a
///     synthetic <see cref="MemberStatusEvent" /> on every <see cref="GuildMember" /> document that
///     was created before Phase 2 shipped (i.e. documents where <c>Status == Unknown</c>).
/// </summary>
internal static class GuildMemberStatusMigration
{
    /// <summary>
    ///     Run the migration.
    /// </summary>
    /// <param name="db">MongoDB.Entities DB instance.</param>
    /// <param name="dryRun">
    ///     When <c>true</c>, log what would be done but do NOT write to the database.
    ///     Safe to run repeatedly for verification.
    /// </param>
    public static async Task RunAsync(DB db, bool dryRun = false)
    {
        Log.Information("GuildMemberStatusMigration starting (dryRun={DryRun})", dryRun);

        List<GuildMember> unmigratedMembers = await db.Find<GuildMember>()
            .ManyAsync(f => f.Or(
                f.Eq(m => m.Status, MemberStatus.Unknown),
                f.Exists(m => m.Status, false)));

        if (unmigratedMembers.Count == 0)
        {
            Log.Information("GuildMemberStatusMigration: no documents to migrate");
            return;
        }

        Log.Information("GuildMemberStatusMigration: found {Count} un-migrated documents", unmigratedMembers.Count);

        Dictionary<MemberStatus, int> histogram = new();

        int skippedCount = 0;

        foreach (GuildMember member in unmigratedMembers)
        {
            // Corrupted documents (e.g. created with a non-string _id before Phase 1) have a
            // null ID after deserialization. SaveAsync would treat them as new documents and try
            // to INSERT, producing a duplicate-key error. Skip them with a warning instead.
            if (string.IsNullOrEmpty(member.ID))
            {
                Log.Warning(
                    "GuildMemberStatusMigration: skipping document with null/empty ID (GuildId={GuildId}, MemberId={MemberId})",
                    member.GuildId, member.MemberId);
                skippedCount++;
                continue;
            }

            (MemberStatus derivedStatus, DateTime derivedAt) = DeriveStatus(member);

            histogram.TryGetValue(derivedStatus, out int existing);
            histogram[derivedStatus] = existing + 1;

            if (dryRun)
            {
                Log.Debug("[DRY RUN] Would migrate {MemberId} -> {Status} at {At}",
                    member.ID, derivedStatus, derivedAt);
                continue;
            }

            Log.Information("Migrating {MemberId} {Unknown} -> {Derived} at {At}",
                member.ID, MemberStatus.Unknown, derivedStatus, derivedAt);

            MemberStatusEvent evt = new()
            {
                From = MemberStatus.Unknown,
                To = derivedStatus,
                At = derivedAt,
                Reason = "migration"
            };

            // Use an atomic $set + $push instead of SaveAsync so we can never accidentally
            // INSERT a document (SaveAsync's insert-vs-replace is decided by HasDefaultID(),
            // which is unreliable for legacy documents with unusual _id shapes).
            // The write-side predicate mirrors the read-side filter: if another writer
            // (e.g. TransitionToAsync) already set a real Status between our Find and this
            // update, the updateOne matches 0 documents and becomes a safe no-op.
            Update<GuildMember> update = db.Update<GuildMember>()
                .MatchID(member.ID)
                .Match(f => f.Or(
                    f.Eq(m => m.Status, MemberStatus.Unknown),
                    f.Exists(m => m.Status, false)))
                .Modify(m => m.Status, derivedStatus)
                .Modify(m => m.StatusChangedAt, (DateTime?)derivedAt)
                .Modify(b => b.Push(m => m.StatusHistory, evt));

            // Mirror flags: keep RemovedByModeration accurate for legacy code paths
            if (derivedStatus is MemberStatus.KickedByModerator or MemberStatus.BannedByModerator
                or MemberStatus.BannedByHoneypot or MemberStatus.BannedExternally
                or MemberStatus.KickedExternally)
            {
                update = update.Modify(m => m.RemovedByModeration, true);
            }

            await update.ExecuteAsync();
        }

        if (skippedCount > 0)
        {
            Log.Warning("GuildMemberStatusMigration: skipped {Count} documents with null/empty ID", skippedCount);
        }

        // Always log the histogram regardless of dry-run
        string histogramText = string.Join(", ",
            histogram.Select(kv => $"{kv.Key}={kv.Value}"));
        Log.Information("GuildMemberStatusMigration {Mode}histogram: {Histogram}",
            dryRun ? "[DRY RUN] " : string.Empty,
            histogramText);

        if (!dryRun)
        {
            Log.Information("GuildMemberStatusMigration complete: migrated {Count} documents", unmigratedMembers.Count);
        }
    }

    /// <summary>
    ///     Derives the most accurate <see cref="MemberStatus" /> and associated timestamp from
    ///     a document's legacy fields. Precedence (highest first):
    ///     BannedAt → KickedAt → AutoKickedAt → LeftAt → FullMember → QuestionnaireSubmitted
    ///     → Onboarding → StrangerRoleRemoved → New.
    /// </summary>
    internal static (MemberStatus status, DateTime at) DeriveStatus(GuildMember m)
    {
        if (m.BannedAt.HasValue)
        {
            MemberStatus s = m.RemovedByModeration ? MemberStatus.BannedByModerator : MemberStatus.BannedExternally;
            return (s, m.BannedAt.Value);
        }

        if (m.KickedAt.HasValue)
        {
            MemberStatus s = m.RemovedByModeration ? MemberStatus.KickedByModerator : MemberStatus.KickedExternally;
            return (s, m.KickedAt.Value);
        }

        if (m.AutoKickedAt.HasValue)
        {
            return (MemberStatus.AutoKicked, m.AutoKickedAt.Value);
        }

        if (m.LeftAt.HasValue)
        {
            return (MemberStatus.LeftVoluntarily, m.LeftAt.Value);
        }

        if (m.PromotedAt.HasValue || m.FullMemberAt.HasValue)
        {
            return (MemberStatus.FullMember, m.PromotedAt ?? m.FullMemberAt!.Value);
        }

        if (m.Application?.QuestionnaireSubmittedAt != null)
        {
            return (MemberStatus.QuestionnaireSubmitted, m.Application.QuestionnaireSubmittedAt.Value);
        }

        if (m.Application is not null || m.Channel is not null)
        {
            DateTime at = m.Application?.CreatedAt ?? m.JoinedAt;
            return (MemberStatus.Onboarding, at);
        }

        if (m.StrangerRoleRemovedAt.HasValue)
        {
            return (MemberStatus.StrangerRoleRemoved, m.StrangerRoleRemovedAt.Value);
        }

        return (MemberStatus.New, m.JoinedAt);
    }
}
