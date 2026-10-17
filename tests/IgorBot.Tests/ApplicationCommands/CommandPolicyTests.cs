using FluentAssertions;

using IgorBot.ApplicationCommands;
using IgorBot.Core;

namespace IgorBot.Tests.ApplicationCommands;

public sealed class CommandAccessPolicyTests
{
    [Fact]
    public void Config_RequiresAdministrator()
    {
        CommandAccessPolicy.IsAuthorized(["Administrator"], CommandAccessPolicy.ConfigPermissions).Should().BeTrue();
        CommandAccessPolicy.IsAuthorized(["SendMessages"], CommandAccessPolicy.ConfigPermissions).Should().BeFalse();
    }

    [Fact]
    public void ApplyMember_RequiresSendMessages()
    {
        CommandAccessPolicy.IsAuthorized(["SendMessages"], CommandAccessPolicy.ApplyMemberPermissions).Should().BeTrue();
        CommandAccessPolicy.IsAuthorized([], CommandAccessPolicy.ApplyMemberPermissions).Should().BeFalse();
    }
}

public sealed class ConfigOptionApplicatorTests
{
    [Theory]
    [InlineData("<@&42>", 42UL)]
    [InlineData("<#7>", 7UL)]
    [InlineData("<@!9>", 9UL)]
    [InlineData("15", 15UL)]
    [InlineData("not-an-id", null)]
    public void ParseSnowflake_ReadsMentions(string value, ulong? expected)
    {
        ConfigOptionApplicator.ParseSnowflake(value).Should().Be(expected);
    }

    [Fact]
    public void TryApply_RoleMention_UpdatesStrangerRole()
    {
        GuildConfig config = new();
        string? error = ConfigOptionApplicator.TryApply(
            config, ConfigOption.StrangerRole, "<@&5>", id => new GuildEntityFacts(GuildEntityKind.Role));

        error.Should().BeNull();
        config.StrangerRoleId.Should().Be(5UL);
    }

    [Fact]
    public void TryApply_MissingRole_Rejects()
    {
        GuildConfig config = new();
        ConfigOptionApplicator.TryApply(
                config, ConfigOption.MemberRole, "<@&5>", _ => new GuildEntityFacts(GuildEntityKind.Missing))
            .Should().Be("Role not found in this server.");
    }

    [Fact]
    public void TryApply_NonCategory_RejectsApplicationCategory()
    {
        GuildConfig config = new();
        ConfigOptionApplicator.TryApply(
                config, ConfigOption.ApplicationCategory, "<#8>", _ => new GuildEntityFacts(GuildEntityKind.TextChannel))
            .Should().Be("Category channel not found or not a category.");
    }

    [Fact]
    public void TryApply_ChannelFormat_RejectsInvalidFormat()
    {
        GuildConfig config = new();
        ConfigOptionApplicator.TryApply(config, ConfigOption.ApplicationChannelFormat, "{", _ => default)
            .Should().StartWith("Invalid format string:");
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void TryApply_BoolOptions_Parse(string value, bool expected)
    {
        GuildConfig config = new();
        ConfigOptionApplicator.TryApply(config, ConfigOption.EnableOnboardingWorkflow, value, _ => default)
            .Should().BeNull();
        config.EnableOnboardingWorkflow.Should().Be(expected);
    }

    [Fact]
    public void TryApply_BoolOption_RejectsOtherText()
    {
        GuildConfig config = new();
        ConfigOptionApplicator.TryApply(config, ConfigOption.AutoAssignStrangerRole, "yes", _ => default)
            .Should().Be("Value must be 'true' or 'false'.");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("soon")]
    public void TryApply_IdleKick_RejectsInvalid(string value)
    {
        GuildConfig config = new();
        ConfigOptionApplicator.TryApply(config, ConfigOption.IdleKickMinutes, value, _ => default)
            .Should().Be("Value must be zero or a positive number of minutes. Zero disables idle kick.");
    }

    [Fact]
    public void TryApply_IdleKick_ZeroDisables()
    {
        GuildConfig config = new() { IdleKickTimeSpan = TimeSpan.FromMinutes(15) };
        ConfigOptionApplicator.TryApply(config, ConfigOption.IdleKickMinutes, "0", _ => default).Should().BeNull();
        config.IdleKickTimeSpan.Should().BeNull();
    }

    [Fact]
    public void TryApply_IdleKick_SetsTimespan()
    {
        GuildConfig config = new();
        ConfigOptionApplicator.TryApply(config, ConfigOption.IdleKickMinutes, "15", _ => default).Should().BeNull();
        config.IdleKickTimeSpan.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Theory]
    [InlineData(ConfigOption.StrangerStatusChannel)]
    [InlineData(ConfigOption.MemberWelcomeChannel)]
    [InlineData(ConfigOption.HoneypotChannel)]
    public void TryApply_TextChannel_Assigns(ConfigOption option)
    {
        GuildConfig config = new();
        ConfigOptionApplicator.TryApply(
                config, option, "<#9>", _ => new GuildEntityFacts(GuildEntityKind.TextChannel))
            .Should().BeNull();

        ulong assigned = option switch
        {
            ConfigOption.StrangerStatusChannel => config.StrangerStatusChannelId,
            ConfigOption.MemberWelcomeChannel => config.MemberWelcomeMessageChannelId,
            ConfigOption.HoneypotChannel => config.HoneypotChannelId ?? 0,
            _ => 0
        };
        assigned.Should().Be(9UL);
    }

    [Theory]
    [InlineData(nameof(GuildEntityKind.Category))]
    [InlineData(nameof(GuildEntityKind.OtherChannel))]
    public void TryApply_NonTextChannel_RejectsStatusWelcomeAndHoneypot(string kindName)
    {
        GuildEntityKind kind = Enum.Parse<GuildEntityKind>(kindName);
        GuildConfig config = new();
        foreach (ConfigOption option in new[]
                 {
                     ConfigOption.StrangerStatusChannel, ConfigOption.MemberWelcomeChannel, ConfigOption.HoneypotChannel
                 })
        {
            ConfigOptionApplicator.TryApply(config, option, "<#9>", _ => new GuildEntityFacts(kind))
                .Should().Be("Channel must be a text channel.");
        }
    }

    [Fact]
    public void TryApply_HoneypotClear_RemovesChannel()
    {
        GuildConfig config = new() { HoneypotChannelId = 4 };
        ConfigOptionApplicator.TryApply(config, ConfigOption.HoneypotChannel, "none", _ => default).Should().BeNull();
        config.HoneypotChannelId.Should().BeNull();
    }

    [Fact]
    public void TryApply_ModeratorRole_DoesNotDuplicate()
    {
        GuildConfig config = new();
        Func<ulong, GuildEntityFacts> role = _ => new GuildEntityFacts(GuildEntityKind.Role);
        ConfigOptionApplicator.TryApply(config, ConfigOption.ModeratorRole, "<@&3>", role);
        ConfigOptionApplicator.TryApply(config, ConfigOption.ModeratorRole, "<@&3>", role);
        config.ApplicationModeratorRoleIds.Should().Equal(3UL);
    }
}

public sealed class QuestionnaireSessionPolicyTests
{
    [Theory]
    [InlineData(false, false, QuestionnaireReplyDisposition.Answer)]
    [InlineData(true, false, QuestionnaireReplyDisposition.TimedOut)]
    [InlineData(false, true, QuestionnaireReplyDisposition.Cancelled)]
    [InlineData(true, true, QuestionnaireReplyDisposition.Cancelled)]
    public void Classify_PrefersCancellation(bool timedOut, bool cancelled, QuestionnaireReplyDisposition expected)
    {
        QuestionnaireSessionPolicy.Classify(timedOut, cancelled).Should().Be(expected);
    }

    [Theory]
    [InlineData(QuestionnaireReplyDisposition.Answer, false)]
    [InlineData(QuestionnaireReplyDisposition.TimedOut, true)]
    [InlineData(QuestionnaireReplyDisposition.Cancelled, true)]
    public void ShouldRearmAutoKick_OnlyForIncompleteReplies(
        QuestionnaireReplyDisposition disposition, bool expected)
    {
        QuestionnaireSessionPolicy.ShouldRearmAutoKick(disposition).Should().Be(expected);
        QuestionnaireSessionPolicy.ShouldRearmAutoKickAfterDmFailure().Should().BeTrue();
        QuestionnaireSessionPolicy.ShouldDisableAutoKickWhileAnswering().Should().BeTrue();
    }

    [Fact]
    public void HasStrangerRole_AndBlockedChannel_MatchMembership()
    {
        QuestionnaireSessionPolicy.HasStrangerRole([1UL, 2UL], 2UL).Should().BeTrue();
        QuestionnaireSessionPolicy.HasStrangerRole([1UL], 2UL).Should().BeFalse();
        QuestionnaireSessionPolicy.IsBlockedChannel([9UL], 9UL).Should().BeTrue();
        QuestionnaireSessionPolicy.IsBlockedChannel([9UL], 8UL).Should().BeFalse();
    }

    [Fact]
    public void DisplayAnswer_AndTimeout_CoverEmptyAndBoundary()
    {
        QuestionnaireSessionPolicy.DisplayAnswer(null).Should().Be("<no value>");
        QuestionnaireSessionPolicy.DisplayAnswer("").Should().Be("<no value>");
        QuestionnaireSessionPolicy.DisplayAnswer("yes").Should().Be("yes");
        QuestionnaireSessionPolicy.ReplyTimeout(3).Should().Be(TimeSpan.FromMinutes(3));
        QuestionnaireSessionPolicy.ReplyTimeout(0).Should().Be(TimeSpan.Zero);
    }
}

public sealed class CommandFailureMapperTests
{
    [Fact]
    public void From_PopulatesDiagnosticFields()
    {
        CommandFailure failure = CommandFailureMapper.From(
            "config", 10UL, 20UL, new InvalidOperationException("boom"));

        failure.CommandName.Should().Be("config");
        failure.GuildId.Should().Be(10UL);
        failure.UserId.Should().Be(20UL);
        failure.ExceptionType.Should().Be(nameof(InvalidOperationException));
        failure.Message.Should().Be("boom");
    }

    [Fact]
    public void From_MissingCommand_UsesUnknown()
    {
        CommandFailureMapper.From("  ", null, null, new OperationCanceledException("stop"))
            .CommandName.Should().Be("<unknown>");
    }
}
