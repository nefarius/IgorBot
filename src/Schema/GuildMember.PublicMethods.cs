using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

using MongoDB.Entities;

using Serilog;

namespace IgorBot.Schema;

internal sealed partial class GuildMember
{
    /// <summary>
    ///     Resets certain properties to defaults. Call when a member (re-)joined.
    /// </summary>
    public void Reset()
    {
        LeftAt = null;
        KickedAt = null;
        BannedAt = null;
        Application = null;
        Channel = null;
    }

    /// <summary>
    ///     Deletes the application widget form the database.
    /// </summary>
    /// <returns></returns>
    public async Task DeleteApplication()
    {
        if (Application is null)
        {
            return;
        }

        await Application.DeleteAsync();

        Application = null;
        await this.SaveAsync();
    }

    /// <summary>
    ///     Deletes the newbie channel form the database.
    /// </summary>
    /// <returns></returns>
    public async Task DeleteChannel()
    {
        if (Channel is null)
        {
            return;
        }

        await Channel.DeleteAsync();
        Channel = null;
        await this.SaveAsync();
    }


    /// <summary>
    ///     Create a newbie channel in the database.
    /// </summary>
    public async Task CreateNewbieChannel(DiscordGuild guild, DiscordChannel channel)
    {
        NewbieChannel newbieChannel =
            new() { GuildId = guild.Id, ChannelId = channel.Id, ChannelName = channel.Name };
        await newbieChannel.SaveAsync();

        Channel = newbieChannel;
        await this.SaveAsync();
    }

    /// <summary>
    ///     Creates the Discord message widget and application a new application.
    /// </summary>
    public async Task CreateApplicationWidget(
        DiscordClient client,
        DiscordGuild guild,
        DiscordChannel strangerStatusChannel
    )
    {
        DiscordEmbedBuilder initialEmbed = GetStatusWidget(client);

        // Create message so we have a unique ID
        DiscordMessage statusMsg = await strangerStatusChannel.SendMessageAsync(initialEmbed);

        // Create application entity
        StrangerApplicationEmbed application = new()
        {
            GuildId = guild.Id, ChannelId = strangerStatusChannel.Id, MessageId = statusMsg.Id
        };

        // generates entity ID
        await application.SaveAsync();

        DiscordMessageBuilder messageBuilder = new DiscordMessageBuilder().AddEmbed(initialEmbed);

        if (!HasLeftGuild)
        {
            Log.Information("Adding ButtonComponents");
            messageBuilder.AddComponents(ButtonComponents);
        }

        if (!HasLeftGuild && application.ButtonComponents.Any())
        {
            Log.Information("Adding application.ButtonComponents");
            messageBuilder.AddComponents(application.ButtonComponents);
        }

        // Add action buttons to message
        await statusMsg.ModifyAsync(messageBuilder);

        // Store in DB
        Application = application;
        await this.SaveAsync();
    }

    /// <summary>
    ///     Updates the Discord message according to the state of this object.
    /// </summary>
    public async Task UpdateApplicationWidget(DiscordClient client, bool isDeleted = false)
    {
        if (Application is null)
        {
            throw new ArgumentNullException(nameof(Application), "Application is null, can't identify status widget.");
        }

        DiscordEmbedBuilder statusEmbed = GetStatusWidget(client);

        DiscordMessageBuilder status = new();

        status.AddEmbed(statusEmbed);

        if (!HasLeftGuild && ButtonComponents.Any())
        {
            status.AddComponents(ButtonComponents);
        }

        if (!isDeleted)
        {
            if (Application.ButtonComponents.Any())
            {
                status.AddComponents(Application.ButtonComponents);
            }
        }

        DiscordGuild guild = client.Guilds[GuildId];

        DiscordMessage statusMessage = await guild
            .GetChannel(Application.ChannelId)
            .GetMessageAsync(Application.MessageId);

        await statusMessage.ModifyAsync(status);
    }

    /// <summary>
    ///     Respond to an interaction with an updated status widget.
    /// </summary>
    public async Task RespondToInteraction(ComponentInteractionCreateEventArgs args, DiscordClient client)
    {
        await args.Interaction.EditOriginalResponseAsync(GetInteractionResponse(client));
    }

    /// <summary>
    ///     Updates the application widget with the last status and removes the application association with this member from
    ///     the DB.
    /// </summary>
    public async Task DeleteApplicationWidget(DiscordClient client)
    {
        await UpdateApplicationWidget(client, true);

        await Application.DeleteAsync();
        Application = null;
        await this.SaveAsync();
    }
}
