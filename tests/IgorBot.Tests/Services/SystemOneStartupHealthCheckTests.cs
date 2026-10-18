using System.Net;

using FluentAssertions;

using IgorBot.Core;
using IgorBot.Services;

using Microsoft.Extensions.Logging;

namespace IgorBot.Tests.Services;

public sealed class SystemOneStartupHealthCheckTests
{
    [Fact]
    public async Task UnconfiguredEndpoint_DoesNotSendARequest()
    {
        RecordingHandler handler = new();
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger, baseUrl: "");

        await check.StartAsync(CancellationToken.None);

        handler.Calls.Should().Be(0);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfiguredEndpoint_SendsHeadToTheBaseUrl()
    {
        RecordingHandler handler = new();
        SystemOneStartupHealthCheck check = Create(handler, new CapturingLogger(), "http://127.0.0.1:9/v1/systemone");

        await check.StartAsync(CancellationToken.None);

        handler.Calls.Should().Be(1);
        handler.Method.Should().Be(HttpMethod.Head);
        handler.RequestUri.Should().Be(new Uri("http://127.0.0.1:9/v1/systemone"));
    }

    [Theory]
    [InlineData("secret-token", "Bearer secret-token")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public async Task AuthorizationHeader_IsSentOnlyForAToken(string? token, string? expected)
    {
        RecordingHandler handler = new();
        SystemOneStartupHealthCheck check = Create(handler, new CapturingLogger(), token: token);

        await check.StartAsync(CancellationToken.None);

        handler.Authorization.Should().Be(expected);
    }

    [Fact]
    public async Task SuccessfulResponse_LogsACredentialSafeEndpoint()
    {
        RecordingHandler handler = new();
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(
            handler,
            logger,
            "http://user:pass@127.0.0.1:9/v1/systemone?x=1",
            token: "secret-token");

        await check.StartAsync(CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("http://127.0.0.1:9/v1/systemone?x=1"));
        handler.RequestUri!.UserInfo.Should().BeEmpty();
        LogEntry entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Be("Reached System One endpoint http://127.0.0.1:9/v1/systemone?x=1");
        entry.Message.Should().NotContain("user:pass");
        entry.Message.Should().NotContain("secret-token");
    }

    [Fact]
    public async Task NonSuccessStatus_LogsAWarningAndContinues()
    {
        RecordingHandler handler = new() { StatusCode = HttpStatusCode.ServiceUnavailable };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger);

        await check.StartAsync(CancellationToken.None);

        LogEntry entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("http://127.0.0.1:9/");
        entry.Message.Should().Contain("503");
        entry.Exception.Should().BeNull();
    }

    [Fact]
    public async Task ConnectionFailure_LogsAWarningAndContinues()
    {
        RecordingHandler handler = new() { Error = new HttpRequestException("connection refused") };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger);

        await check.StartAsync(CancellationToken.None);

        LogEntry entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Exception.Should().BeOfType<HttpRequestException>();
        entry.Message.Should().Contain("could not be reached");
    }

    [Fact]
    public async Task Timeout_LogsAWarningAndContinues()
    {
        RecordingHandler handler = new() { Error = new TaskCanceledException("timed out") };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger);

        await check.StartAsync(CancellationToken.None);

        LogEntry entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Exception.Should().BeOfType<TaskCanceledException>();
    }

    [Fact]
    public async Task HostCancellation_PropagatesWithoutAWarning()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();
        RecordingHandler handler = new();
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger);

        Func<Task> act = () => check.StartAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Entries.Should().BeEmpty();
    }

    private static SystemOneStartupHealthCheck Create(
        HttpMessageHandler handler,
        CapturingLogger logger,
        string baseUrl = "http://127.0.0.1:9",
        string? token = null)
    {
        SystemOneSettings settings = SystemOneSettings.Create(new SystemOneConfig
        {
            BaseUrl = baseUrl,
            ApiToken = token,
            TimeoutSeconds = 2
        });
        return new SystemOneStartupHealthCheck(settings, logger, handler);
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger : ILogger<SystemOneStartupHealthCheck>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? Authorization { get; private set; }

        public Exception? Error { get; init; }

        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            cancellationToken.ThrowIfCancellationRequested();
            if (Error is not null)
            {
                throw Error;
            }

            return Task.FromResult(new HttpResponseMessage(StatusCode));
        }
    }
}
