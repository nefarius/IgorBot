using System.Threading.Channels;

using IgorBot.Core;

namespace IgorBot.Services;

internal sealed class OnboardingQueue(ChannelWriter<NewMemberMessage> writer) : IOnboardingQueue
{
    public ValueTask EnqueueAsync(NewMemberMessage message, CancellationToken ct = default)
    {
        return writer.WriteAsync(message, ct);
    }
}