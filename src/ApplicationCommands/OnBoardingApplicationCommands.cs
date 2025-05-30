using System.Diagnostics.CodeAnalysis;

using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Util;

using Microsoft.Extensions.Options;

using MongoDB.Entities;

namespace IgorBot.ApplicationCommands;

[SlashCommandGroup("apply", "Apply for server membership.")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public sealed class OnBoardingApplicationCommands : ApplicationCommandModule
{
    [SlashRequirePermissions(Permissions.SendMessages)]
    [SlashCommand("member", "Apply for regular membership.")]
    public async Task Member(
        InteractionContext ctx
    )
    {
        #region Validation

        ILogger<BaseDiscordClient> logger = ctx.Client.Logger;

        logger.LogInformation("{Member} started command", ctx.Member);

        await ctx.DeferAsync();

        GuildMember dbMember = await DB.Find<GuildMember>().OneAsync(ctx.ToEntityId());

        if (dbMember is null)
        {
            logger.LogError("{Member} not found in database", ctx.Member);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Missing database entry",
                Description =
                    $"{ctx.Member.Mention}: your account is missing from the database. Go whip a staff member!",
                Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        StrangerApplicationEmbed application = dbMember.Application;

        if (application is null)
        {
            logger.LogError("Application widget for {Member} not found in database", ctx.Member);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Missing application widget",
                Description =
                    $"{ctx.Member.Mention}: your application widget is missing from the database. Go whip a staff member!",
                Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        application.IsAutoKickEnabled = false;
        await dbMember.Application.SaveAsync();
        await dbMember.SaveAsync();
        await dbMember.UpdateApplicationWidget(ctx.Client);

        #endregion

        #region Questionaire logic

        IOptionsMonitor<IgorConfig> config = ctx.Services.GetRequiredService<IOptionsMonitor<IgorConfig>>();
        DiscordGuild guild = ctx.Guild;
        GuildConfig guildConfig = config.CurrentValue.Guilds[guild.Id.ToString()];
        Questionnaire questionnaire = guildConfig.Questionnaires["Member"];
        DiscordMember member = (DiscordMember)ctx.User;

        if (member.Roles.All(r => r.Id != guildConfig.StrangerRoleId))
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Missing role",
                Description =
                    $"{ctx.Member.Mention}: this command isn't allowed without a certain role!",
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

            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
        }

        DiscordChannel interactionChannel;

        try
        {
            //
            // Conduct either in the current channel or DMs
            // 
            interactionChannel =
                questionnaire.ConductInPrivate
                    ? await ctx.Member.CreateDmChannelAsync()
                    : // start questionnaire in DMs
                    ctx.Channel; // start questionnaire in the channel of the invoked command
        }
        catch (UnauthorizedException)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Couldn't DM member",
                Description =
                    $":+1: {ctx.Member.Mention} I couldn't send you a DM, please adjust your privacy settings!",
                Color = new DiscordColor(0xFF0000)
            }));
            return;
        }

        //
        // Check if this channel is allowed
        // 
        if (questionnaire.BlockedChannelIds.Contains(interactionChannel.Id))
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Invalid channel",
                Description = $":no_entry: Questionnaire `{questionnaire.Name}` not allowed in this channel.",
                Color = DiscordColor.Red
            }));
            return;
        }

        //
        // Notify user that action is happening in DMs
        //  
        if (questionnaire.ConductInPrivate)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
            {
                Title = "Questionnaire started",
                Description = $"{ctx.Member.Mention} I've started the questionnaire in your DMs.",
                Color = new DiscordColor(0x00FF00)
            }));
        }
        else
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
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

            InteractivityResult<DiscordMessage> response = await interactionChannel.GetNextMessageAsync(
                ctx.User,
                TimeSpan.FromMinutes(questionnaire.TimeoutMinutes)
            );

            //
            // Bail out if too slow to answer
            //
            if (response.TimedOut)
            {
                string error = $"{ctx.Member.Mention} too slow, your questionnaire has timed out, please try again!";

                if (questionnaire.ConductInPrivate)
                {
                    await interactionChannel.SendMessageAsync(error);
                }
                else
                {
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(new DiscordEmbedBuilder
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

            if (string.IsNullOrEmpty(response.Result.Content))
            {
                logger.LogError("Message result empty {Message}", response.Result);
            }

            responses.Add(response.Result.Content);
        }

        DiscordEmbedBuilder submissionEmbed = new()
        {
            Title = "Questionnaire submission",
            Description = $"Questionnaire: {Formatter.Bold(questionnaire.Name)}, Author: {ctx.Member.Mention}",
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
                    string.IsNullOrEmpty(responses[i]) ? "<no value>" : responses[i]);
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
            submission.AddComponents(questionnaire.ActionButtons.Select(b =>
                new DiscordButtonComponent(
                    b.Style,
                    $"{b.CustomId}|{questionnaire.Id}|{ctx.Member.Id}",
                    b.Label,
                    b.IsDisabled
                )));
        }

        DiscordChannel submissionChannel = ctx.Guild.GetChannel(questionnaire.SubmissionChannelId);

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
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
            return;
        }

        logger.LogInformation("{User} finished questionnaire {Name}", ctx.Member, questionnaire.Name);

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
                $"Nicely done {ctx.Member.Mention}, I've submitted your answers to {submissionChannel.Mention}. " +
                "Please be patient until a moderator acknowledges it.",
            Color = new DiscordColor(0x00FF00)
        });

        logger.LogInformation("{Member} finished application command", ctx.Member);

        #endregion

        #region Application widget logic

        application.QuestionnaireSubmittedAt = DateTime.UtcNow;

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