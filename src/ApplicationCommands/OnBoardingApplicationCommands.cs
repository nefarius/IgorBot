using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;
using IgorBot.Util;

using MongoDB.Entities;

namespace IgorBot.ApplicationCommands;

[Command("apply"), Description("Apply for server membership.")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public sealed class OnBoardingApplicationCommands(DB db, IGuildConfigService guildConfigService)
{
    [RequirePermissions(DiscordPermission.SendMessages)]
    [Command("member"), Description("Apply for regular membership.")]
    public async Task Member(
        SlashCommandContext ctx
    )
    {
        #region Validation

        ILogger logger = ctx.Client.Logger;

        logger.LogInformation("{Member} started command", ctx.RequireMember());

        await ctx.DeferResponseAsync();

        GuildMember? dbMember = await db.Find<GuildMember>().OneAsync(ctx.ToEntityId());

        if (dbMember is null)
        {
            logger.LogError("{Member} not found in database", ctx.RequireMember());
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Missing database entry",
                Description =
                    $"{ctx.RequireMember().Mention}: your account is missing from the database. Go whip a staff member!",
                Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        StrangerApplicationEmbed? application = dbMember.Application;

        if (application is null)
        {
            logger.LogError("Application widget for {Member} not found in database", ctx.RequireMember());
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Missing application widget",
                Description =
                    $"{ctx.RequireMember().Mention}: your application widget is missing from the database. Go whip a staff member!",
                Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        #endregion

        #region Questionaire logic

        DiscordGuild guild = ctx.RequireGuild();
        GuildConfig? guildConfig = await guildConfigService.GetAsync(guild.Id);
        if (guildConfig == null)
        {
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Server not configured",
                Description =
                    "This server is not configured yet. An administrator can run `/config setup` to get started.",
                Color = new DiscordColor(0xFFAA00)
            }));
            return;
        }

        if (!guildConfig.Questionnaires.TryGetValue("Member", out Questionnaire? questionnaire) || questionnaire == null)
        {
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Questionnaire not configured",
                Description = "The Member questionnaire is not set up. Ask an administrator to configure it.",
                Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        DiscordMember member = (DiscordMember)ctx.User;

        if (!QuestionnaireSessionPolicy.HasStrangerRole(member.Roles.Select(r => r.Id), guildConfig.StrangerRoleId))
        {
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Missing role",
                Description =
                    $"{ctx.RequireMember().Mention}: this command isn't allowed without a certain role!",
                Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        if (questionnaire.Questions.Count == 0)
        {
            DiscordEmoji emoji = DiscordEmoji.FromName(ctx.Client, ":no_entry:");

            DiscordEmbedBuilder embed = new()
            {
                Title = "Questionnaire has no questions",
                Description =
                    $"{emoji} Questionnaire `{questionnaire.Name}` has no questions to ask. Go whip a staff member!",
                Color = new DiscordColor(0xFF0000)
            };

            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(embed));
        }

        DiscordChannel interactionChannel;

        try
        {
            //
            // Conduct either in the current channel or DMs
            // 
            interactionChannel =
                questionnaire.ConductInPrivate
                    ? await ctx.RequireMember().CreateDmChannelAsync()
                    : // start questionnaire in DMs
                    ctx.Channel; // start questionnaire in the channel of the invoked command
        }
        catch (UnauthorizedException)
        {
            if (QuestionnaireSessionPolicy.ShouldRearmAutoKickAfterDmFailure())
            {
                application.IsAutoKickEnabled = true;
                await db.SaveAsync(application);
                await db.SaveAsync(dbMember);
            }

            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Couldn't DM member",
                Description =
                    $":+1: {ctx.RequireMember().Mention} I couldn't send you a DM, please adjust your privacy settings!",
                Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        //
        // Check if this channel is allowed
        // 
        if (QuestionnaireSessionPolicy.IsBlockedChannel(questionnaire.BlockedChannelIds, interactionChannel.Id))
        {
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Invalid channel",
                Description = $":no_entry: Questionnaire `{questionnaire.Name}` not allowed in this channel.",
                Color = DiscordColor.Red
            }));
            return;
        }

        //
        // Disable auto-kick for the duration of the questionnaire so the idle
        // timer does not fire while the member is actively answering.
        //
        if (QuestionnaireSessionPolicy.ShouldDisableAutoKickWhileAnswering())
        {
            application.IsAutoKickEnabled = false;
            await db.SaveAsync(application);
            await db.SaveAsync(dbMember);
            await dbMember.UpdateApplicationWidget(ctx.Client);
        }

        //
        // Notify user that action is happening in DMs
        //  
        if (questionnaire.ConductInPrivate)
        {
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Questionnaire started",
                Description = $"{ctx.RequireMember().Mention} I've started the questionnaire in your DMs.",
                Color = new DiscordColor(0x00FF00)
            }));
        }
        else
        {
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Let's go!",
                Description =
                    $"You've got {questionnaire.TimeoutMinutes} minutes to answer each question. Good luck!",
                Color = new DiscordColor(0x00FF00)
            }));
        }

        //
        // Cache responses
        // 
        List<string> responses = new(questionnaire.Questions.Count);

        //
        // Request/reply on question after another
        // 
        for (int i = 0; i < questionnaire.Questions.Count; i++)
        {
            Question question = questionnaire.Questions[i];

            await interactionChannel.TriggerTypingAsync();

            await interactionChannel.SendMessageAsync(
                $"{i + 1}/{questionnaire.Questions.Count}. {question.Content}");

            QuestionnaireReplyDisposition disposition;
            string? answer = null;
            try
            {
                InteractivityResult<DiscordMessage> response = await interactionChannel.GetNextMessageAsync(
                    ctx.User,
                    QuestionnaireSessionPolicy.ReplyTimeout(questionnaire.TimeoutMinutes)
                );
                disposition = QuestionnaireSessionPolicy.Classify(response.TimedOut, cancelled: false);
                if (disposition == QuestionnaireReplyDisposition.Answer)
                {
                    answer = response.Result.Content;
                }
            }
            catch (OperationCanceledException)
            {
                disposition = QuestionnaireSessionPolicy.Classify(timedOut: false, cancelled: true);
            }

            if (QuestionnaireSessionPolicy.ShouldRearmAutoKick(disposition))
            {
                application.IsAutoKickEnabled = true;
                await db.SaveAsync(application);
                await db.SaveAsync(dbMember);

                if (disposition == QuestionnaireReplyDisposition.Cancelled)
                {
                    return;
                }

                string error = $"{ctx.RequireMember().Mention} too slow, your questionnaire has timed out, please try again!";

                if (questionnaire.ConductInPrivate)
                {
                    await interactionChannel.SendMessageAsync(error);
                }
                else
                {
                    await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(new DiscordEmbedBuilder
                    {
                        Title = "Timeout", Description = error, Color = DiscordColor.IndianRed
                    }));

                    await interactionChannel.SendMessageAsync(new DiscordEmbedBuilder
                    {
                        Title = "Timeout", Description = error, Color = DiscordColor.IndianRed
                    });
                }

                return;
            }

            if (string.IsNullOrEmpty(answer))
            {
                logger.LogError("Message result empty for question {Index}", i);
            }

            responses.Add(answer ?? string.Empty);
        }

        DiscordEmbedBuilder submissionEmbed = new()
        {
            Title = "Questionnaire submission",
            Description =
                $"Questionnaire: {Formatter.Bold(questionnaire.Name)}, " +
                $"Author: {ctx.RequireMember()} ({ctx.RequireMember().Mention}), " +
                $"Status panel: {application.MessageMentionUrl}",
            Timestamp = DateTimeOffset.UtcNow,
            Footer = new DiscordEmbedBuilder.EmbedFooter
            {
                IconUrl = ctx.Client.CurrentUser.AvatarUrl, Text = "Questionnaire by Igor"
            }
        };

        try
        {
            for (int i = 0; i < questionnaire.Questions.Count; i++)
            {
                submissionEmbed.AddField(questionnaire.Questions[i].Content,
                    QuestionnaireSessionPolicy.DisplayAnswer(responses[i]));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add fields to submission embed");
        }

        //
        // Build the submission message with Embed as primary content
        // 
        DiscordMessageBuilder submission = new DiscordMessageBuilder()
            .AddEmbed(submissionEmbed);

        //
        // If configured, add action buttons
        // 
        if (questionnaire.ActionButtons.Count != 0)
        {
            // TODO: currently not used, remove or finish implementation
            submission.AddActionRowComponent(questionnaire.ActionButtons.Select(b =>
                new DiscordButtonComponent(
                    b.Style,
                    $"{b.CustomId}|{questionnaire.Id}|{ctx.RequireMember().Id}",
                    b.Label,
                    b.IsDisabled
                )));
        }

        DiscordChannel? submissionChannel = ctx.RequireGuild().FindChannel(questionnaire.SubmissionChannelId);

        if (submissionChannel is null)
        {
            DiscordEmoji emoji = DiscordEmoji.FromName(ctx.Client, ":no_entry:");

            DiscordEmbedBuilder embed = new()
            {
                Title = "Submission channel not found",
                Description =
                    $"{emoji} Submission channel with ID `{questionnaire.SubmissionChannelId}` doesn't seem to exist.",
                Color = new DiscordColor(0xFF0000) // red
            };
            await ctx.EditResponseAsync(new DiscordMessageBuilder().AddEmbed(embed));
            return;
        }

        logger.LogInformation("{User} finished questionnaire {Name}", ctx.RequireMember(), questionnaire.Name);

        try
        {
            //
            // All prepared, send the submission message
            // 
            await submissionChannel.SendMessageAsync(submission);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send submission message");
        }

        //
        // Inform the user of success
        // 
        await interactionChannel.SendMessageAsync(new DiscordEmbedBuilder
        {
            Title = "Questionnaire submitted",
            Description =
                $"Nicely done {ctx.RequireMember().Mention}, I've submitted your answers to {submissionChannel.Mention}. " +
                "Please be patient until a moderator acknowledges it.",
            Color = new DiscordColor(0x00FF00)
        });

        logger.LogInformation("{Member} finished application command", ctx.RequireMember());

        #endregion

        #region Application widget logic

        application.QuestionnaireSubmittedAt = DateTime.UtcNow;

        // Persist the submission timestamp so the Promote button remains visible
        // after a bot restart and so the auto-kick query filter stays correct.
        await db.SaveAsync(application);
        await dbMember.TransitionToAsync(db, MemberStatus.QuestionnaireSubmitted);

        logger.LogInformation("Updating status message {Id}", application.MessageId);

        //
        // Update moderator status embed
        // 
        try
        {
            await dbMember.UpdateApplicationWidget(ctx.Client);
        }
        catch (NotFoundException ex)
        {
            logger.LogError(ex, "Failed to find status message to modify");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected exception");
        }

        #endregion
    }
}