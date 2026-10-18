using System.Diagnostics;
using System.Net;
using System.Text;

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

        await RunUntilSettled(check);

        handler.Calls.Should().Be(0);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfiguredEndpoint_GetsTheHealthPath()
    {
        RecordingHandler handler = new();
        SystemOneStartupHealthCheck check = Create(handler, new CapturingLogger(), "http://127.0.0.1:9/v1?x=1");

        await RunUntilSettled(check);

        handler.Calls.Should().Be(1);
        handler.Method.Should().Be(HttpMethod.Get);
        handler.RequestUri.Should().Be(new Uri("http://127.0.0.1:9/health"));
    }

    [Theory]
    [InlineData("secret-token", "Bearer secret-token")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public async Task AuthorizationHeader_IsSentOnlyForAToken(string? token, string? expected)
    {
        RecordingHandler handler = new();
        SystemOneStartupHealthCheck check = Create(handler, new CapturingLogger(), token: token);

        await RunUntilSettled(check);

        handler.Authorization.Should().Be(expected);
    }

    [Fact]
    public async Task HealthyResponse_LogsACredentialSafeEndpoint()
    {
        RecordingHandler handler = new();
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(
            handler,
            logger,
            "http://user:pass@127.0.0.1:9/v1/systemone?x=1",
            token: "secret-token");

        await RunUntilSettled(check);

        handler.RequestUri.Should().Be(new Uri("http://127.0.0.1:9/health"));
        handler.RequestUri!.UserInfo.Should().BeEmpty();
        LogEntry entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Be("Reached System One endpoint http://127.0.0.1:9/health");
        entry.Message.Should().NotContain("user:pass");
        entry.Message.Should().NotContain("secret-token");
    }

    [Fact]
    public async Task NonSuccessStatus_LogsAWarningAndContinues()
    {
        RecordingHandler handler = new() { StatusCode = HttpStatusCode.ServiceUnavailable };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger);

        await RunUntilSettled(check);

        LogEntry entry = Warnings(logger).Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("http://127.0.0.1:9/health");
        entry.Message.Should().Contain("503");
        entry.Exception.Should().BeNull();
        handler.Calls.Should().Be(7);
    }

    [Fact]
    public async Task NonSuccessStatus_DoesNotWaitForTheBody()
    {
        RecordingHandler handler = new()
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
            HoldBody = true
        };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger, timeoutSeconds: 1);
        Stopwatch watch = Stopwatch.StartNew();

        await RunUntilSettled(check);

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
        LogEntry entry = Warnings(logger).Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("503");
        entry.Message.Should().NotContain("could not be reached");
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("""{"state":"ok"}""")]
    [InlineData("""{"status":1}""")]
    [InlineData("[]")]
    [InlineData("\"ok\"")]
    [InlineData("null")]
    public async Task UnreadableHealthResponse_LogsAWarning(string body)
    {
        RecordingHandler handler = new() { Body = body };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger);

        await RunUntilSettled(check);

        LogEntry entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("unreadable health response");
    }

    [Fact]
    public async Task OversizedHealthBody_LogsAnUnreadableResponse()
    {
        RecordingHandler handler = new()
        {
            Body = "{\"status\":\"ok\",\"padding\":\"" + new string('x', 1100) + "\"}"
        };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger);

        await RunUntilSettled(check);

        LogEntry entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("unreadable health response");
    }

    [Fact]
    public async Task OtherHealthStatus_LogsAWarning()
    {
        RecordingHandler handler = new() { Body = """{"status":"loading"}""" };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger);

        await RunUntilSettled(check);

        LogEntry entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("loading");
        entry.Message.Should().NotContain("ok");
    }

    [Fact]
    public async Task ConnectionFailure_LogsAWarningAndContinues()
    {
        RecordingHandler handler = new() { Error = new HttpRequestException("connection refused") };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger);

        await RunUntilSettled(check);

        LogEntry entry = Warnings(logger).Should().ContainSingle().Subject;
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

        await RunUntilSettled(check);

        LogEntry entry = Warnings(logger).Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Exception.Should().BeOfType<TaskCanceledException>();
    }

    [Fact]
    public async Task SlowResponseBody_IsBoundedByTheConfiguredTimeout()
    {
        RecordingHandler handler = new() { HoldBody = true };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger, timeoutSeconds: 1);

        await RunUntilSettled(check).WaitAsync(TimeSpan.FromSeconds(4));

        LogEntry entry = Warnings(logger).Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("could not be reached");
    }

    [Fact]
    public async Task SlowEndpoint_ReturnsBeforeTheResponseAndCancelsOnShutdown()
    {
        RecordingHandler handler = new() { Hold = true };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger);

        Task starting = check.StartAsync(CancellationToken.None);

        starting.IsCompleted.Should().BeTrue();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        logger.Entries.Should().BeEmpty();

        await check.StopAsync(CancellationToken.None);

        logger.Entries.Should().BeEmpty();
        handler.Calls.Should().Be(1);
        handler.Method.Should().Be(HttpMethod.Get);
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
        handler.Calls.Should().Be(0);
        logger.Entries.Should().BeEmpty();
        await check.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TransientTimeout_IsRetriedUntilTheEndpointResponds()
    {
        RecordingHandler handler = new() { Error = new TaskCanceledException("timed out"), ErrorCalls = 2 };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger);

        await RunUntilSettled(check);

        handler.Calls.Should().Be(3);
        logger.Entries.Count(e => e.Level == LogLevel.Information && e.Message.Contains("retry")).Should().Be(2);
        Warnings(logger).Should().BeEmpty();
        logger.Entries[^1].Message.Should().Be("Reached System One endpoint http://127.0.0.1:9/health");
    }

    [Fact]
    public async Task ClientError_IsNotRetried()
    {
        RecordingHandler handler = new() { StatusCode = HttpStatusCode.NotFound };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger);

        await RunUntilSettled(check);

        handler.Calls.Should().Be(1);
        Warnings(logger).Should().ContainSingle().Which.Message.Should().Contain("404");
    }

    [Fact]
    public async Task StopDuringBackoff_CancelsWithoutAWarning()
    {
        RecordingHandler handler = new() { Error = new HttpRequestException("down") };
        CapturingLogger logger = new();
        SystemOneStartupHealthCheck check = Create(handler, logger, retryDelay: TimeSpan.FromMinutes(5));

        await check.StartAsync(CancellationToken.None);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await check.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Warnings(logger).Should().BeEmpty();
        handler.Calls.Should().Be(1);
    }

    private static List<LogEntry> Warnings(CapturingLogger logger) =>
        logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();

    private static async Task RunUntilSettled(SystemOneStartupHealthCheck check)
    {
        await check.StartAsync(CancellationToken.None);
        await check.ProbeCompletion;
        await check.StopAsync(CancellationToken.None);
    }

    private static SystemOneStartupHealthCheck Create(
        HttpMessageHandler handler,
        CapturingLogger logger,
        string baseUrl = "http://127.0.0.1:9",
        string? token = null,
        int timeoutSeconds = 2,
        TimeSpan? retryDelay = null)
    {
        SystemOneSettings settings = SystemOneSettings.Create(new SystemOneConfig
        {
            BaseUrl = baseUrl,
            ApiToken = token,
            TimeoutSeconds = timeoutSeconds
        });
        return new SystemOneStartupHealthCheck(settings, logger, handler, retryDelay ?? TimeSpan.Zero);
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

        public int ErrorCalls { get; init; } = int.MaxValue;

        public bool Hold { get; init; }

        public bool HoldBody { get; init; }

        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        public string Body { get; init; } = """{"status":"ok"}""";

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Started.TrySetResult(true);
            if (Hold)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Error is not null && Calls <= ErrorCalls)
            {
                throw Error;
            }

            HttpContent content = HoldBody
                ? new StreamContent(new PendingBody())
                : new StringContent(Body, Encoding.UTF8, "application/json");
            return new HttpResponseMessage(StatusCode)
            {
                Content = content
            };
        }
    }

    private sealed class PendingBody : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
