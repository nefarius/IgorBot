using Coravel.Queuing.Interfaces;

using IgorBot.Invocables;

namespace IgorBot.Services;

public class StartupTasks : BackgroundService
{
    private readonly IQueue _queue;

    public StartupTasks(IQueue queue)
    {
        _queue = queue;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _queue.QueueInvocable<MemberDbSyncInvokable>();

        return Task.CompletedTask;
    }
}