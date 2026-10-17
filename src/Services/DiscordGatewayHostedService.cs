using DSharpPlus;

namespace IgorBot.Services;

internal interface IDiscordGateway
{
    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}

internal sealed class DiscordClientGateway(DiscordClient client) : IDiscordGateway
{
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return client.ConnectAsync();
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return client.DisconnectAsync();
    }
}

/// <summary>
///     Connects the Discord gateway for the lifetime of the generic host.
/// </summary>
internal sealed class DiscordGatewayHostedService(
    IDiscordGateway gateway,
    IDiscordReadinessService readiness,
    ILogger<DiscordGatewayHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        readiness.Reset();
        logger.LogInformation("Connecting Discord gateway");
        await gateway.ConnectAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Disconnecting Discord gateway");
        await gateway.DisconnectAsync(cancellationToken);
        readiness.Reset();
    }
}
