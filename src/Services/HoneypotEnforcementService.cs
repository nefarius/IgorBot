using System.Collections.Concurrent;

using IgorBot.Schema;

using MongoDB.Entities;

namespace IgorBot.Services;

/// <inheritdoc />
internal sealed class HoneypotEnforcementService(
    DB db,
    IHoneypotBanClient banClient,
    ILogger<HoneypotEnforcementService> logger) : IHoneypotEnforcementService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _memberLocks = new();

    /// <inheritdoc />
    public async Task<HoneypotEnforcementOutcome> EnforceAsync(
        ulong guildId,
        ulong memberId,
        string memberDisplay,
        string mention)
    {
        string key = $"{guildId}-{memberId}";
        SemaphoreSlim gate = _memberLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await EnforceCoreAsync(guildId, memberId, memberDisplay, mention);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<HoneypotEnforcementOutcome> EnforceCoreAsync(
        ulong guildId,
        ulong memberId,
        string memberDisplay,
        string mention)
    {
        string entityId = $"{guildId}-{memberId}";
        GuildMember? existing = await db.Find<GuildMember>().OneAsync(entityId);

        if (existing is not null && HoneypotRules.ShouldSkipDiscordBan(existing.Status))
        {
            logger.LogInformation(
                "Honeypot skip {Member}: already {Status}",
                memberDisplay, existing.Status);
            return HoneypotEnforcementOutcome.AlreadyBanned;
        }

        if (existing is not null && existing.Status == MemberStatus.BannedByHoneypot)
        {
            return await RetryExistingHoneypotBanAsync(guildId, memberId, memberDisplay);
        }

        bool isNewDocument = existing is null;
        GuildMember guildMember = existing ?? new GuildMember
        {
            GuildId = guildId,
            MemberId = memberId,
            Member = memberDisplay,
            Mention = mention
        };

        if (isNewDocument)
        {
            await db.SaveAsync(guildMember);
        }

        MemberStatus previousStatus = guildMember.Status;

        logger.LogInformation(
            "Honeypot triggered by {Member} (existing document: {Existing}, prior status {Previous})",
            memberDisplay, !isNewDocument, previousStatus);

        await guildMember.TransitionToAsync(db, MemberStatus.BannedByHoneypot, "honeypot");

        logger.LogInformation("Banning {Member} due to messaging in honeypot channel", memberDisplay);

        try
        {
            await banClient.BanAsync(guildId, memberId);
        }
        catch (Exception banEx)
        {
            if (HoneypotRules.IsPermanentBanFailure(banEx))
            {
                logger.LogError(banEx,
                    "Permanent honeypot ban failure for {Member}; not retrying", memberDisplay);
                return HoneypotEnforcementOutcome.PermanentFailure;
            }

            logger.LogError(banEx, "BanAsync failed for honeypot member {Member}", memberDisplay);

            bool? stillPresent = await banClient.IsMemberPresentAsync(guildId, memberId);
            if (stillPresent == false)
            {
                logger.LogWarning(
                    "Member {Member} is no longer in the guild after failed BanAsync; keeping BannedByHoneypot",
                    memberDisplay);
                return HoneypotEnforcementOutcome.MemberGone;
            }

            logger.LogError(
                "BanAsync failed for honeypot member {Member}, reverting DB state (present={Present})",
                memberDisplay, stillPresent);

            try
            {
                MemberStatus revertTo = isNewDocument ? MemberStatus.New : previousStatus;
                await guildMember.TransitionToAsync(db, revertTo, "revert-honeypot-ban");
            }
            catch (Exception revertEx)
            {
                logger.LogError(revertEx,
                    "Failed to revert DB state for {Member} after BanAsync failure", memberDisplay);
            }

            return HoneypotEnforcementOutcome.Failed;
        }

        logger.LogInformation("{Member} banned", memberDisplay);
        return HoneypotEnforcementOutcome.Banned;
    }

    private async Task<HoneypotEnforcementOutcome> RetryExistingHoneypotBanAsync(
        ulong guildId,
        ulong memberId,
        string memberDisplay)
    {
        logger.LogInformation(
            "Retrying Discord ban for {Member} already marked BannedByHoneypot",
            memberDisplay);

        try
        {
            await banClient.BanAsync(guildId, memberId);
            return HoneypotEnforcementOutcome.AlreadyBanned;
        }
        catch (Exception banEx)
        {
            if (HoneypotRules.IsPermanentBanFailure(banEx))
            {
                logger.LogError(banEx,
                    "Permanent honeypot ban failure for already-marked member {Member}; not retrying",
                    memberDisplay);
                return HoneypotEnforcementOutcome.PermanentFailure;
            }

            logger.LogError(banEx,
                "Retry BanAsync failed for already-marked honeypot member {Member}", memberDisplay);

            bool? stillPresent = await banClient.IsMemberPresentAsync(guildId, memberId);
            return stillPresent == false
                ? HoneypotEnforcementOutcome.MemberGone
                : HoneypotEnforcementOutcome.Failed;
        }
    }
}
