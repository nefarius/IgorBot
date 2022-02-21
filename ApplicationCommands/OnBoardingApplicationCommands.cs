using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using IgorBot.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IgorBot.ApplicationCommands
{
    [SlashCommandGroup("apply", "Apply for server membership.")]
    public class OnBoardingApplicationCommands : ApplicationCommandModule
    {
        [SlashRequirePermissions(Permissions.SendMessages)]
        [SlashCommand("member", "Apply for regular membership.")]
        public async Task Member(
            InteractionContext ctx
        )
        {
            var config = ctx.Services.GetRequiredService<IgorConfig>();
            var guild = ctx.Guild;
            var guildConfig = config.Guilds[guild.Id.ToString()];
            var questionnaire = guildConfig.Questionnaires["Member"];
            var member = (DiscordMember)ctx.User;

            if (member.Roles.All(r => r.Id != guildConfig.StrangerRoleId))
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().AddEmbed(new DiscordEmbedBuilder
                    {
                        Title = "Missing role",
                        Description =
                            $"{ctx.Member.Mention}: this command isn't allowed without a certain role!",
                        Color = new DiscordColor(0xFF0000)
                    }).AsEphemeral());
                return;
            }

            if (!questionnaire.Questions.Any())
            {
                var emoji = DiscordEmoji.FromName(ctx.Client, ":no_entry:");

                var embed = new DiscordEmbedBuilder
                {
                    Title = "Questionnaire has no questions",
                    Description =
                        $"{emoji} Questionnaire `{questionnaire.Name}` has no questions to ask. Go whip a staff member!",
                    Color = new DiscordColor(0xFF0000)
                };

                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral());
            }

            DiscordChannel interactionChannel;

            try
            {
                //
                // Conduct either in current channel or DMs
                // 
                interactionChannel =
                    questionnaire.ConductInPrivate
                        ? await ctx.Member.CreateDmChannelAsync()
                        : // start questionnaire in DMs
                        ctx.Channel; // start questionnaire in the channel of the invoked command
            }
            catch (UnauthorizedException)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().AddEmbed(new DiscordEmbedBuilder
                    {
                        Title = "Couldn't DM member",
                        Description =
                            $":+1: {ctx.Member.Mention} I couldn't send you a DM, please adjust your privacy settings!",
                        Color = new DiscordColor(0xFF0000)
                    }).AsEphemeral());
                return;
            }

            //
            // Check if this channel is allowed
            // 
            if (questionnaire.BlockedChannelIds.Contains(interactionChannel.Id))
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().AddEmbed(new DiscordEmbedBuilder
                    {
                        Title = "Invalid channel",
                        Description = $":no_entry: Questionnaire `{questionnaire.Name}` not allowed in this channel.",
                        Color = DiscordColor.Red
                    }).AsEphemeral());
                return;
            }

            //
            // Notify user that action is happening in DMs
            //  
            if (questionnaire.ConductInPrivate)
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().AddEmbed(new DiscordEmbedBuilder
                    {
                        Title = "Questionnaire started",
                        Description = $"{ctx.Member.Mention} I've started the questionnaire in your DMs.",
                        Color = new DiscordColor(0x00FF00)
                    }).AsEphemeral());
            else
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().AddEmbed(new DiscordEmbedBuilder
                    {
                        Title = "Let's go!",
                        Description =
                            $"You've got {questionnaire.TimeoutMinutes} minutes to answer each question. Good luck!",
                        Color = new DiscordColor(0x00FF00)
                    }).AsEphemeral());

            //
            // Cache responses
            // 
            var responses = new List<string>(questionnaire.Questions.Count);

            //
            // Request/reply on question after another
            // 
            for (var i = 0; i < questionnaire.Questions.Count; i++)
            {
                var question = questionnaire.Questions[i];

                await interactionChannel.TriggerTypingAsync();

                await interactionChannel.SendMessageAsync(
                    $"{i + 1}/{questionnaire.Questions.Count}. {question.Content}");

                var response = await interactionChannel.GetNextMessageAsync(
                    ctx.User,
                    TimeSpan.FromMinutes(questionnaire.TimeoutMinutes)
                );

                //
                // Bail out if too slow to answer
                //
                if (response.TimedOut)
                {
                    var error = $"{ctx.Member.Mention} too slow, your questionnaire has timed out, please try again!";

                    if (questionnaire.ConductInPrivate)
                        await interactionChannel.SendMessageAsync(error);
                    else
                        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                            new DiscordInteractionResponseBuilder().AddEmbed(new DiscordEmbedBuilder
                            {
                                Title = "Timeout",
                                Description = error,
                                Color = DiscordColor.IndianRed
                            }).AsEphemeral());
                    return;
                }

                responses.Add(response.Result.Content);
            }

            var submissionEmbed = new DiscordEmbedBuilder
            {
                Title = "Questionnaire submission",
                Description = $"Questionnaire: {Formatter.Bold(questionnaire.Name)}, Author: {ctx.Member.Mention}",
                Timestamp = DateTimeOffset.UtcNow,
                Footer = new DiscordEmbedBuilder.EmbedFooter
                {
                    IconUrl = ctx.Client.CurrentUser.AvatarUrl,
                    Text = "Questionnaire by Igor"
                }
            };

            for (var i = 0; i < questionnaire.Questions.Count; i++)
                submissionEmbed.AddField(questionnaire.Questions[i].Content, responses[i]);

            //
            // Build submission message with Embed as primary content
            // 
            var submission = new DiscordMessageBuilder()
                .WithEmbed(submissionEmbed);

            //
            // If configured, add action buttons
            // 
            if (questionnaire.ActionButtons.Any())
                submission.AddComponents(questionnaire.ActionButtons.Select(b =>
                    new DiscordButtonComponent(
                        b.Style,
                        $"{b.CustomId}|{questionnaire.Id}|{ctx.Member.Id}",
                        b.Label,
                        b.IsDisabled
                    )));

            var submissionChannel = ctx.Guild.GetChannel(questionnaire.SubmissionChannelId);

            if (submissionChannel is null)
            {
                var emoji = DiscordEmoji.FromName(ctx.Client, ":no_entry:");

                var embed = new DiscordEmbedBuilder
                {
                    Title = "Submission channel not found",
                    Description =
                        $"{emoji} Submission channel with ID `{questionnaire.SubmissionChannelId}` doesn't seem to exist.",
                    Color = new DiscordColor(0xFF0000) // red
                };
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().AddEmbed(embed));
                return;
            }

            ctx.Client.Logger.LogInformation("{User} finished questionnaire {Name}", ctx.Member, questionnaire.Name);

            //
            // All prepared, send submission message
            // 
            await submissionChannel.SendMessageAsync(submission);

            //
            // Inform the user of success
            // 
            await interactionChannel.SendMessageAsync(new DiscordEmbedBuilder
            {
                Title = "Questionnaire submitted",
                Description =
                    $"Nicely done {ctx.Member.Mention}, I've submitted your answers to {submissionChannel.Mention}. Please be patient until a moderator acknowledges it.",
                Color = new DiscordColor(0x00FF00)
            });
        }
    }
}