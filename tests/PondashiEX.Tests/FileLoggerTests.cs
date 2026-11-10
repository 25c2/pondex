using PondashiEX.Utility;

namespace PondashiEX.Tests;

public sealed class FileLoggerTests
{
    [Fact]
    public async Task DetailedLogging_IsOptInAndCanBeDisabledWithoutLosingNormalLogs()
    {
        var writer = new StringWriter();
        await using (var logger = new FileLogger(writer, queueCapacity: 16))
        {
            Assert.False(logger.DetailedLoggingEnabled);
            logger.Debug("hidden before");
            logger.DetailedLoggingEnabled = true;
            logger.Debug("visible\nsecond line");
            logger.DetailedLoggingEnabled = false;
            logger.Debug("hidden after");
            logger.Info("normal information");
            logger.Warning("normal warning");
            logger.Error("normal error", new IOException("test exception"));
        }

        var output = writer.ToString();
        Assert.DoesNotContain("hidden", output);
        Assert.Contains("[DEBUG] visible\\nsecond line", output);
        Assert.Contains("詳細ログ: ON", output);
        Assert.Contains("詳細ログ: OFF", output);
        Assert.Contains("[INFO] normal information", output);
        Assert.Contains("[WARN] normal warning", output);
        Assert.Contains("[ERROR] normal error", output);
        Assert.Contains("test exception", output);
    }

    [Fact]
    public async Task Messages_AreFlushedWhileApplicationIsStillRunning()
    {
        var writer = new FlushTrackingWriter();
        await using var logger = new FileLogger(writer, queueCapacity: 8);
        logger.DetailedLoggingEnabled = true;
        logger.Debug("live diagnostic");

        var output = await writer.DiagnosticFlushed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("[DEBUG] live diagnostic", output);
    }

    [Fact]
    public async Task MessageControls_AreEscapedOntoOnePhysicalLine()
    {
        var writer = new StringWriter();
        var logger = new FileLogger(writer, queueCapacity: 8);

        logger.Info("音源\r\n2026-01-01 [ERROR] 偽装\t\u0001\u2028\u2029\u202E");
        await logger.DisposeAsync();

        var output = writer.ToString();
        Assert.Contains("音源\\r\\n2026-01-01 [ERROR] 偽装\\t\\u0001\\u2028\\u2029\\u202E", output);
        Assert.Single(output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task WriterFailure_DoesNotEscapeFromDispose()
    {
        var logger = new FileLogger(new ThrowingTextWriter(), queueCapacity: 2);
        logger.Error("write failure test");

        var exception = await Record.ExceptionAsync(async () => await logger.DisposeAsync());

        Assert.Null(exception);
        Assert.True(logger.Diagnostics.IsDisabled);
        Assert.Equal(1, logger.Diagnostics.WriteFailures);
    }

    private sealed class FlushTrackingWriter : StringWriter
    {
        public TaskCompletionSource<string> DiagnosticFlushed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task FlushAsync()
        {
            var output = ToString();
            if (output.Contains("[DEBUG] live diagnostic", StringComparison.Ordinal)) { DiagnosticFlushed.TrySetResult(output); }
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingTextWriter : StringWriter
    {
        public override Task WriteLineAsync(string? value) =>
            Task.FromException(new IOException("destination unavailable"));

    }
}
