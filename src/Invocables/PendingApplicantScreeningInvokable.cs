using Coravel.Invocable;
using Coravel.Queuing.Interfaces;

using IgorBot.Services;

namespace IgorBot.Invocables;

internal sealed class PendingApplicantScreeningInvokable(IPendingApplicantScreeningService screening)
    : IInvocable, ICancellableInvocable, ICancellableTask
{
    public CancellationToken CancellationToken { get; set; }

    public CancellationToken Token
    {
        get => CancellationToken;
        set => CancellationToken = value;
    }

    public Task Invoke() => screening.ScreenAllAsync(CancellationToken);
}
