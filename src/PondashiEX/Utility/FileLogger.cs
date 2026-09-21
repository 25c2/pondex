using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace PondashiEX.Utility;

public sealed class FileLogger : IAppLogger, IAsyncDisposable
{
    private const int DefaultQueueCapacity = 1_024;
    private readonly Channel<string> _messages;
    private readonly TextWriter _writer;
    private readonly Task _writerTask;
    private int _disabled;
    private int _disposeStarted;

    public FileLogger(string logDirectory)
        : this(CreateWriter(logDirectory), DefaultQueueCapacity)
    {
    }

    internal FileLogger(TextWriter writer, int queueCapacity)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueCapacity);
        _writer = writer;
        _messages = Channel.CreateBounded<string>(new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public void Info(string message) => Enqueue("INFO", message);

    public void Warning(string message) => Enqueue("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message} | {exception}";
        Enqueue("ERROR", detail);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _messages.Writer.TryComplete();
        try
        {
            await _writerTask.ConfigureAwait(false);
        }
        catch
        {
            // Logging must never prevent a normal application shutdown.
        }

        try
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // The log destination can disappear (for example, a removed USB drive).
        }
    }

    internal static string Sanitize(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var result = new StringBuilder(message.Length);
        foreach (var character in message)
        {
            switch (character)
            {
                case '\r':
                    result.Append("\\r");
                    break;
                case '\n':
                    result.Append("\\n");
                    break;
                case '\t':
                    result.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        result.Append("\\u");
                        result.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        result.Append(character);
                    }

                    break;
            }
        }

        return result.ToString();
    }

    private static StreamWriter CreateWriter(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"PondashiEX-{DateTime.Now:yyyyMMdd}.log");
        return new StreamWriter(
            new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false
        };
    }

    private void Enqueue(string level, string message)
    {
        if (Volatile.Read(ref _disabled) != 0 || Volatile.Read(ref _disposeStarted) != 0)
        {
            return;
        }

        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {Sanitize(message)}";
        _messages.Writer.TryWrite(line);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await _writer.WriteLineAsync(message).ConfigureAwait(false);
            }

            await _writer.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Exchange(ref _disabled, 1);
            while (_messages.Reader.TryRead(out _))
            {
                // Discard pending messages after the destination becomes unavailable.
            }
        }
    }
}
