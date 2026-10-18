using System.Net.Http.Headers;

namespace IgorBot.Services;

/// <summary>
///     Confirms the configured System One base URL is reachable without blocking startup.
/// </summary>
internal sealed class SystemOneStartupHealthCheck : IHostedService
{
    private readonly SystemOneSettings _settings;
    private readonly ILogger<SystemOneStartupHealthCheck> _logger;
    private readonly HttpMessageHandler? _handler;

    public SystemOneStartupHealthCheck(SystemOneSettings settings, ILogger<SystemOneStartupHealthCheck> logger)
        : this(settings, logger, null)
    {
    }

    internal SystemOneStartupHealthCheck(
        SystemOneSettings settings,
        ILogger<SystemOneStartupHealthCheck> logger,
        HttpMessageHandler? handler)
    {
        _settings = settings;
        _logger = logger;
        _handler = handler;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_settings.IsConfigured || _settings.BaseUri is null)
        {
            return;
        }

        Uri endpoint = RequestUri(_settings.BaseUri);
        string reportedEndpoint = SafeEndpoint(_settings.BaseUri);
        using HttpClient client = CreateClient();
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Head, endpoint);
            if (_settings.ApiToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiToken);
            }

            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if ((int)response.StatusCode is >= 200 and <= 299)
            {
                _logger.LogInformation("Reached System One endpoint {Endpoint}", reportedEndpoint);
                return;
            }

            _logger.LogWarning(
                "System One endpoint {Endpoint} returned HTTP {StatusCode}",
                reportedEndpoint,
                (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "System One endpoint {Endpoint} could not be reached", reportedEndpoint);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private HttpClient CreateClient()
    {
        HttpMessageHandler handler = _handler ?? new HttpClientHandler();
        return new HttpClient(handler, disposeHandler: _handler is null)
        {
            Timeout = _settings.Timeout
        };
    }

    private static Uri RequestUri(Uri endpoint) =>
        string.IsNullOrEmpty(endpoint.UserInfo) ? endpoint : new Uri(SafeEndpoint(endpoint));

    internal static string SafeEndpoint(Uri endpoint) =>
        endpoint.GetComponents(
            UriComponents.Scheme |
            UriComponents.Host |
            UriComponents.Port |
            UriComponents.Path |
            UriComponents.Query |
            UriComponents.KeepDelimiter,
            UriFormat.SafeUnescaped);
}
