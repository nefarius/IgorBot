using System.Threading.Channels;

using IgorBot.Core;
using IgorBot.Handlers;

namespace IgorBot.Services;

internal sealed class OnboardingQueueProcessor(
    ChannelReader<NewMemberMessage> reader,
    NewMemberHandler handler,
    ILogger<OnboardingQueueProcessor> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Onboarding queue processor started");

        await foreach (NewMemberMessage message in reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await handler.ProcessAsync(message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing new member message for {MemberEntryId}", message.MemberEntryId);
            }
        }
    }
}
