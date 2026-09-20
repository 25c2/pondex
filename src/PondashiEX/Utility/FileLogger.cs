using System.Text;
using System.Threading.Channels;

namespace PondashiEX.Utility;

public sealed class FileLogger : IAppLogger, IAsyncDisposable
{
    private readonly Channel<string> _messages = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly StreamWriter _writer;
    private readonly Task _writerTask;

    public FileLogger(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"PondashiEX-{DateTime.Now:yyyyMMdd}.log");
        _writer = new StreamWriter(
            new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false
        };
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public void Info(string message) => Enqueue("INFO", message);

    public void Warning(string message) => Enqueue("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Enqueue("ERROR", detail);
    }

    public async ValueTask DisposeAsync()
    {
        _messages.Writer.TryComplete();
        await _writerTask.ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
    }

    private void Enqueue(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
        _messages.Writer.TryWrite(line);
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var message in _messages.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            await _writer.WriteLineAsync(message).ConfigureAwait(false);
        }

        await _writer.FlushAsync().ConfigureAwait(false);
    }
}

