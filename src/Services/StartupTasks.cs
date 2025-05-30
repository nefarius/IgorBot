using Coravel.Queuing.Interfaces;

using IgorBot.Invocables;

namespace IgorBot.Services;

public class StartupTasks(IQueue queue) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        queue.QueueInvocable<MemberDbSyncInvokable>();

        return Task.CompletedTask;
    }
}