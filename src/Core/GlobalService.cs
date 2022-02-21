using System.Diagnostics.CodeAnalysis;

namespace IgorBot.Core;

[SuppressMessage("ReSharper", "NotAccessedField.Local")]
internal class GlobalService : BackgroundService
{
    private readonly Global _global;

    public GlobalService(Global global)
    {
        _global = global;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }
}
