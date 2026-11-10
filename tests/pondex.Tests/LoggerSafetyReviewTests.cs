using pondex.Utility;

namespace pondex.Tests;

public sealed class LoggerSafetyReviewTests
{
    [Fact]
    public async Task SlowStorage_DebugFloodCannotEvictWarningsErrorsOrBlockProducers()
    {
        var writer = new BlockingWriter();
        await using var logger = new FileLogger(writer, 8) { DetailedLoggingEnabled = true };
        try
        {
            await writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            logger.Warning("important warning");
            logger.Error("important error");
            await Task.Run(() =>
            {
                for (var i = 0; i < 10000; i++) { logger.Debug($"flood {i}"); }
                logger.Info("important info");
                logger.Error("last error before exit");
            }).WaitAsync(TimeSpan.FromSeconds(5));
            var stats = logger.Diagnostics;
            Assert.InRange(stats.Pending, 1, 8);
            Assert.True(stats.DroppedDebug > 9900);
            Assert.Equal(0, stats.DroppedInfo);
            Assert.Equal(0, stats.DroppedWarning);
            Assert.Equal(0, stats.DroppedError);
        }
        finally { writer.Release.TrySetResult(); }
        await logger.DisposeAsync();
        var output = writer.ToString();
        Assert.Contains("important warning", output);
        Assert.Contains("important error", output);
        Assert.Contains("important info", output);
        Assert.Contains("last error before exit", output);
        Assert.Contains("ログ破棄（累計）", output);
        Assert.True(output.IndexOf("important warning", StringComparison.Ordinal) < output.IndexOf("important error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FullCriticalQueue_ProtectsErrorsAndCountsUnavoidableLoss()
    {
        var writer = new BlockingWriter();
        await using var logger = new FileLogger(writer, 2);
        logger.Info("hold writer");
        try
        {
            await writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            logger.Error("first error");
            logger.Error("second error");
            logger.Warning("discard warning");
            logger.Info("discard info");
            logger.Error("latest error");
            Assert.Equal(1, logger.Diagnostics.DroppedWarning);
            Assert.Equal(1, logger.Diagnostics.DroppedInfo);
            Assert.Equal(1, logger.Diagnostics.DroppedError);
        }
        finally { writer.Release.TrySetResult(); }
        await logger.DisposeAsync();
        Assert.DoesNotContain("first error", writer.ToString());
        Assert.Contains("second error", writer.ToString());
        Assert.Contains("latest error", writer.ToString());
    }

    [Fact]
    public async Task Rotation_UsesRecordDateAcrossBacklogAndMultiDaySleep()
    {
        var clock = new ManualTime(new DateTimeOffset(2026, 11, 10, 23, 59, 59, TimeSpan.Zero));
        var first = new BlockingWriter();
        var writers = new Dictionary<DateOnly, StringWriter>();
        await using var logger = new FileLogger(date =>
        {
            var writer = writers.Count == 0 ? first : new StringWriter();
            writers.Add(date, writer);
            return writer;
        }, clock, 16);
        logger.Info("before midnight first");
        try
        {
            await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            logger.Info("before midnight second");
            clock.Now = clock.Now.AddSeconds(1);
            logger.Info("at midnight");
            clock.Now = clock.Now.AddDays(3);
            logger.Error("after sleep");
            clock.Now = clock.Now.AddDays(2); // Processing time must not choose the file.
        }
        finally { first.Release.TrySetResult(); }
        await logger.DisposeAsync();
        Assert.Equal(3, writers.Count);
        Assert.Contains("before midnight first", writers[new(2026, 11, 10)].ToString());
        Assert.Contains("before midnight second", writers[new(2026, 11, 10)].ToString());
        Assert.Contains("2026-11-11 00:00:00.000", writers[new(2026, 11, 11)].ToString());
        Assert.Contains("at midnight", writers[new(2026, 11, 11)].ToString());
        Assert.Contains("after sleep", writers[new(2026, 11, 14)].ToString());
        var all = string.Concat(writers.Values.Select(writer => writer.ToString()));
        Assert.Equal(4, all.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task ClockMovedBackwards_ReopensExistingDateInAppendMode()
    {
        using var directory = new TestDirectory();
        var clock = new ManualTime(new DateTimeOffset(2026, 11, 10, 23, 59, 59, TimeSpan.Zero));
        await using var logger = new FileLogger(date => new StreamWriter(directory.GetPath($"{date:yyyyMMdd}.log"), append: true), clock);
        logger.Info("one");
        clock.Now = clock.Now.AddDays(1);
        logger.Info("two");
        clock.Now = clock.Now.AddDays(-1);
        logger.Info("three");
        await logger.DisposeAsync();
        var earlier = await File.ReadAllLinesAsync(directory.GetPath("20261110.log"));
        Assert.Equal(2, earlier.Length);
        Assert.EndsWith("one", earlier[0]);
        Assert.EndsWith("three", earlier[1]);
        Assert.Single(await File.ReadAllLinesAsync(directory.GetPath("20261111.log")));
    }

    [Fact]
    public async Task RotationFailure_DisablesLoggingAndDoesNotEscapeOrBlockLaterCalls()
    {
        var clock = new ManualTime(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        var first = new StringWriter();
        await using var logger = new FileLogger(date => date.Day == 10 ? first : throw new IOException("removed disk"), clock);
        logger.Info("retained");
        clock.Now = clock.Now.AddDays(1);
        logger.Error("rotation fails");
        await logger.DisposeAsync();
        Assert.Contains("retained", first.ToString());
        Assert.True(logger.Diagnostics.IsDisabled);
        Assert.True(logger.Diagnostics.WriteFailures > 0);
        Assert.Equal(1, logger.Diagnostics.DroppedError);
        logger.Error("ignored safely");
        await logger.DisposeAsync();
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class BlockingWriter : StringWriter
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task WriteLineAsync(string? value)
        {
            Entered.TrySetResult();
            await Release.Task;
            await base.WriteLineAsync(value);
        }
    }
}
