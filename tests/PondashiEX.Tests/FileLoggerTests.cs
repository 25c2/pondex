using PondashiEX.Utility;

namespace PondashiEX.Tests;

public sealed class FileLoggerTests
{
    [Fact]
    public async Task MessageControls_AreEscapedOntoOnePhysicalLine()
    {
        var writer = new StringWriter();
        var logger = new FileLogger(writer, queueCapacity: 8);

        logger.Info("音源\r\n2026-01-01 [ERROR] 偽装\t\u0001");
        await logger.DisposeAsync();

        var output = writer.ToString();
        Assert.Contains("音源\\r\\n2026-01-01 [ERROR] 偽装\\t\\u0001", output);
        Assert.Single(output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task WriterFailure_DoesNotEscapeFromDispose()
    {
        var logger = new FileLogger(new ThrowingTextWriter(), queueCapacity: 2);
        logger.Error("write failure test");

        var exception = await Record.ExceptionAsync(async () => await logger.DisposeAsync());

        Assert.Null(exception);
    }

    private sealed class ThrowingTextWriter : StringWriter
    {
        public override Task WriteLineAsync(string? value) =>
            Task.FromException(new IOException("destination unavailable"));

    }
}
