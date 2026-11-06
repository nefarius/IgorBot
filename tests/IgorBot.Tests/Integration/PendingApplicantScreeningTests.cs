using FluentAssertions;

using IgorBot.Schema;
using IgorBot.Services;

using MongoDB.Entities;

namespace IgorBot.Tests.Integration;

[Xunit.Collection("Mongo")]
public sealed class PendingApplicantScreeningTests(MongoFixture mongo)
{
    [Fact]
    public async Task TryRecordAsync_ConcurrentScansReservePromotionOnlyOnce()
    {
        DB db = await mongo.CreateDatabaseAsync($"igor-pending-{Guid.NewGuid():N}");
        GuildMember snapshot = await InsertApplicantAsync(db);
        GuildMember other = (await db.Find<GuildMember>().OneAsync(snapshot.ID))!;
        OnboardingDecisionRecord decision = new()
        {
            Outcome = "AutoPromote", AutomationAttempted = true, EvaluatedAt = DateTime.UtcNow
        };

        bool[] results = await Task.WhenAll(
            PendingApplicantScreeningService.TryRecordAsync(db, snapshot, decision, 7, 8),
            PendingApplicantScreeningService.TryRecordAsync(db, other, decision, 7, 8));

        results.Count(r => r).Should().Be(1);
        GuildMember saved = (await db.Find<GuildMember>().OneAsync(snapshot.ID))!;
        saved.Application!.Decision!.AutomationAttempted.Should().BeTrue();
        saved.Application.SubmissionChannelId.Should().Be(7);
        saved.Application.SubmissionMessageId.Should().Be(8);
        saved.Application.QuestionnaireSubmittedAt.Should().Be(snapshot.Application!.QuestionnaireSubmittedAt);
    }

    [Theory]
    [InlineData(MemberStatus.FullMember)]
    [InlineData(MemberStatus.LeftVoluntarily)]
    [InlineData(MemberStatus.BannedByModerator)]
    public async Task TryRecordAsync_DoesNotOverwriteModeratorOrLifecycleChanges(MemberStatus next)
    {
        DB db = await mongo.CreateDatabaseAsync($"igor-pending-{Guid.NewGuid():N}");
        GuildMember snapshot = await InsertApplicantAsync(db);
        await snapshot.TransitionToAsync(db, next);
        // The job still holds the old pre-classification snapshot.
        snapshot.Status = MemberStatus.QuestionnaireSubmitted;

        (await PendingApplicantScreeningService.TryRecordAsync(db, snapshot,
            new OnboardingDecisionRecord { Outcome = "AutoPromote", AutomationAttempted = true }, 7, 8))
            .Should().BeFalse();
        (await db.Find<GuildMember>().OneAsync(snapshot.ID))!.Status.Should().Be(next);
    }

    [Fact]
    public async Task TryRecordAsync_DoesNotApplyOldAnswersToResubmittedApplication()
    {
        DB db = await mongo.CreateDatabaseAsync($"igor-pending-{Guid.NewGuid():N}");
        GuildMember snapshot = await InsertApplicantAsync(db);
        await db.Update<GuildMember>().MatchID(snapshot.ID)
            .Modify(m => m.Application!.QuestionnaireSubmittedAt, (DateTime?)DateTime.UtcNow.AddMinutes(5))
            .ExecuteAsync();

        (await PendingApplicantScreeningService.TryRecordAsync(db, snapshot,
            new OnboardingDecisionRecord { Outcome = "AutoPromote", AutomationAttempted = true }, 7, 8))
            .Should().BeFalse();
    }

    private static async Task<GuildMember> InsertApplicantAsync(DB db)
    {
        GuildMember member = new()
        {
            ID = "1-42", GuildId = 1, MemberId = 42, Status = MemberStatus.QuestionnaireSubmitted,
            Member = "applicant", Mention = "<@42>"
        };
        await db.SaveAsync(member);
        StrangerApplicationEmbed application = new()
        {
            ID = "1-2-3", GuildId = 1, ChannelId = 2, MessageId = 3,
            QuestionnaireSubmittedAt = DateTime.UtcNow
        };
        await db.Update<GuildMember>().MatchID(member.ID).Modify(m => m.Application, application).ExecuteAsync();
        return (await db.Find<GuildMember>().OneAsync(member.ID))!;
    }
}
