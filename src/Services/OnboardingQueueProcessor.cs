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
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Onboarding queue processor started");

        await foreach (NewMemberMessage message in reader.ReadAllAsync(stoppingToken))
        {
            int attempt = 0;

            while (attempt <= MaxRetries)
            {
                try
                {
                    await handler.ProcessAsync(message);
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    attempt++;

                    if (attempt <= MaxRetries)
                    {
                        logger.LogWarning(ex,
                            "Attempt {Attempt}/{Total} failed for {MemberEntryId}, retrying in {Delay}",
                            attempt, MaxRetries + 1, message.MemberEntryId, RetryDelay);
                        await Task.Delay(RetryDelay, stoppingToken);
                    }
                    else
                    {
                        logger.LogError(ex,
                            "Permanently failed to process new member message for {MemberEntryId} after {Total} attempts",
                            message.MemberEntryId, MaxRetries + 1);
                    }
                }
            }
        }
    }
}
