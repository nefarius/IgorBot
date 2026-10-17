using System.Reflection;

using DSharpPlus.Entities;
using DSharpPlus.Entities.AuditLogs;

using FluentAssertions;

using IgorBot.Modules;
using IgorBot.Schema;
using IgorBot.Services;

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

public sealed class ExternalKickPolicyTests
{
    [Fact]
    public void TryRead_MapsKickEntry()
    {
        DiscordAuditLogKickEntry kick = Create<DiscordAuditLogKickEntry>();
        DiscordMember target = Create<DiscordMember>();
        Set(target, nameof(DiscordMember.Id), 20UL);
        DiscordUser actor = Create<DiscordUser>();
        Set(actor, nameof(DiscordUser.Id), 30UL);
        Set(kick, nameof(DiscordAuditLogKickEntry.Target), target);
        Set(kick, nameof(DiscordAuditLogKickEntry.UserResponsible), actor);
        Set(kick, nameof(DiscordAuditLogKickEntry.Reason), "spam");

        ExternalKickPolicy.TryRead(kick, out ExternalKickObservation observed).Should().BeTrue();
        observed.TargetId.Should().Be(20UL);
        observed.ActorId.Should().Be(30UL);
        observed.Reason.Should().Be("spam");
    }

    [Fact]
    public void TryRead_MissingTarget_Rejects()
    {
        ExternalKickPolicy.TryRead(Create<DiscordAuditLogKickEntry>(), out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_MissingActor_LeavesActorEmpty()
    {
        DiscordAuditLogKickEntry kick = Create<DiscordAuditLogKickEntry>();
        DiscordMember target = Create<DiscordMember>();
        Set(target, nameof(DiscordMember.Id), 20UL);
        Set(kick, nameof(DiscordAuditLogKickEntry.Target), target);

        ExternalKickPolicy.TryRead(kick, out ExternalKickObservation observed).Should().BeTrue();
        observed.TargetId.Should().Be(20UL);
        observed.ActorId.Should().BeNull();
        observed.Reason.Should().BeNull();
    }

    [Fact]
    public void GatewayIntents_IncludeGuildModeration()
    {
        IgorDiscordRegistration.GatewayIntents.HasFlag(DSharpPlus.DiscordIntents.GuildModeration).Should().BeTrue();
    }

    private static T Create<T>() where T : class =>
        (T)Activator.CreateInstance(typeof(T), nonPublic: true)!;

    private static void Set(object target, string propertyName, object? value)
    {
        PropertyInfo property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        property.SetValue(target, value);
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
