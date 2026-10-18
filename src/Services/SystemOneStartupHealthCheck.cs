using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace IgorBot.Services;

/// <summary>
///     Confirms the configured System One server is healthy without blocking startup.
/// </summary>
internal sealed class SystemOneStartupHealthCheck : IHostedService
{
    private const int MaximumHealthBodyLength = 1024;
    private readonly SystemOneSettings _settings;
    private readonly ILogger<SystemOneStartupHealthCheck> _logger;
    private readonly HttpMessageHandler? _handler;
    private readonly CancellationTokenSource _shutdown = new();
    private int _stopped;
    private Task? _probe;

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

    internal Task ProbeCompletion => _probe ?? Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_settings.IsConfigured || _settings.BaseUri is null)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        CancellationTokenSource probeCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        _probe = RunProbeAsync(probeCancellation, _settings.BaseUri);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        try
        {
            await _shutdown.CancelAsync();
            if (_probe is not null)
            {
                await _probe.WaitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            if (_probe is null || _probe.IsCompleted)
            {
                _shutdown.Dispose();
            }
        }
    }

    private async Task RunProbeAsync(CancellationTokenSource probeCancellation, Uri baseUri)
    {
        using (probeCancellation)
        {
            await ProbeAsync(baseUri, probeCancellation.Token);
        }
    }

    private async Task ProbeAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        Uri endpoint = HealthUri(baseUri);
        string reportedEndpoint = SafeEndpoint(endpoint);
        using HttpClient client = CreateClient();
        try
        {
            using HttpResponseMessage response = await SendAsync(client, endpoint, cancellationToken);
            if ((int)response.StatusCode is < 200 or > 299)
            {
                _logger.LogWarning(
                    "System One endpoint {Endpoint} returned HTTP {StatusCode}",
                    reportedEndpoint,
                    (int)response.StatusCode);
                return;
            }

            string body = response.Content is null
                ? ""
                : await ReadHealthBodyAsync(response.Content, cancellationToken);
            if (!TryReadStatus(body, out string? status))
            {
                _logger.LogWarning(
                    "System One endpoint {Endpoint} returned an unreadable health response",
                    reportedEndpoint);
                return;
            }

            if (!string.Equals(status, "ok", StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "System One endpoint {Endpoint} reported health status {Status}",
                    reportedEndpoint,
                    status);
                return;
            }

            _logger.LogInformation("Reached System One endpoint {Endpoint}", reportedEndpoint);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Startup was aborted or the host is stopping. Reachability is no longer relevant.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "System One endpoint {Endpoint} could not be reached", reportedEndpoint);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        if (_settings.ApiToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiToken);
        }

        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<string> ReadHealthBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_settings.Timeout);
        await using Stream stream = await content.ReadAsStreamAsync(timeout.Token);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[256];
        while (true)
        {
            int read = await stream.ReadAsync(chunk.AsMemory(), timeout.Token);
            if (read == 0)
            {
                return Encoding.UTF8.GetString(buffer.ToArray());
            }

            if (buffer.Length + read > MaximumHealthBodyLength)
            {
                return "";
            }

            buffer.Write(chunk, 0, read);
        }
    }

    private static bool TryReadStatus(string body, out string? status)
    {
        status = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("status", out JsonElement property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            status = property.GetString();
            return !string.IsNullOrWhiteSpace(status);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private HttpClient CreateClient()
    {
        HttpMessageHandler handler = _handler ?? new HttpClientHandler();
        return new HttpClient(handler, disposeHandler: _handler is null)
        {
            Timeout = _settings.Timeout
        };
    }

    internal static Uri HealthUri(Uri baseUri)
    {
        UriBuilder builder = new(baseUri)
        {
            Path = "/health",
            Query = "",
            Fragment = "",
            UserName = "",
            Password = ""
        };
        return builder.Uri;
    }

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
