using FluentAssertions;

using IgorBot.Modules;
using IgorBot.Schema;

namespace IgorBot.Tests.Modules;

public sealed class ComponentInteractionParserTests
{
    [Theory]
    [InlineData("strangers|abc|kick", "strangers", "abc", "kick")]
    [InlineData("strangers|guild-user|ban", "strangers", "guild-user", "ban")]
    [InlineData("strangers|id|promote", "strangers", "id", "promote")]
    [InlineData("strangers|id|disable-auto-kick", "strangers", "id", "disable-auto-kick")]
    public void TryParse_WellFormedId_ReturnsRoute(string customId, string category, string id, string action)
    {
        ComponentInteractionParser.TryParse(customId, out ComponentInteractionRoute route).Should().BeTrue();
        route.Category.Should().Be(category);
        route.DatabaseId.Should().Be(id);
        route.Action.Should().Be(action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("strangers")]
    [InlineData("strangers|only-two")]
    public void TryParse_TooFewFields_Rejects(string? customId)
    {
        ComponentInteractionParser.TryParse(customId, out _).Should().BeFalse();
    }
}

public sealed class AuditLogKickParserTests
{
    [Fact]
    public void Parse_MatchingKick_ReturnsIds()
    {
        const string json = """
            {"action_type":20,"guild_id":"10","target_id":"20","user_id":"30","reason":"spam"}
            """;

        AuditLogKickDecision decision = AuditLogKickParser.Parse(AuditLogKickParser.EventName, json);

        decision.Status.Should().Be(AuditLogKickParseStatus.Kick);
        decision.GuildId.Should().Be(10UL);
        decision.TargetId.Should().Be(20UL);
        decision.ActorId.Should().Be(30UL);
        decision.Reason.Should().Be("spam");
    }

    [Fact]
    public void Parse_UnrelatedAction_IsNotKick()
    {
        AuditLogKickParser.Parse(AuditLogKickParser.EventName, """{"action_type":22,"guild_id":"1","target_id":"2"}""")
            .Status.Should().Be(AuditLogKickParseStatus.NotKick);
    }

    [Fact]
    public void Parse_OtherEvent_IsIgnored()
    {
        AuditLogKickParser.Parse("MESSAGE_CREATE", """{"action_type":20}""")
            .Status.Should().Be(AuditLogKickParseStatus.IgnoredEvent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{"action_type":"kick"}""")]
    public void Parse_MalformedPayload_IsMalformed(string? json)
    {
        AuditLogKickParser.Parse(AuditLogKickParser.EventName, json)
            .Status.Should().Be(AuditLogKickParseStatus.Malformed);
    }

    [Fact]
    public void Parse_MissingGuild_ReportsMissingGuild()
    {
        AuditLogKickParser.Parse(AuditLogKickParser.EventName, """{"action_type":20,"target_id":"2"}""")
            .Status.Should().Be(AuditLogKickParseStatus.MissingGuildId);
    }

    [Fact]
    public void Parse_MissingTarget_ReportsMissingTarget()
    {
        AuditLogKickParser.Parse(AuditLogKickParser.EventName, """{"action_type":20,"guild_id":"1"}""")
            .Status.Should().Be(AuditLogKickParseStatus.MissingTargetId);
    }

    [Theory]
    [InlineData(MemberStatus.New, true)]
    [InlineData(MemberStatus.Onboarding, true)]
    [InlineData(MemberStatus.QuestionnaireSubmitted, true)]
    [InlineData(MemberStatus.FullMember, true)]
    [InlineData(MemberStatus.StrangerRoleRemoved, true)]
    [InlineData(MemberStatus.Unknown, true)]
    [InlineData(MemberStatus.KickedByModerator, false)]
    [InlineData(MemberStatus.BannedByHoneypot, false)]
    [InlineData(MemberStatus.LeftVoluntarily, false)]
    public void ShouldPreMark_OnlyNonTerminalStatuses(MemberStatus status, bool expected)
    {
        ExternalKickPolicy.ShouldPreMark(status).Should().Be(expected);
    }
}
