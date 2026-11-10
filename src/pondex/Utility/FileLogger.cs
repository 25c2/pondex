using System.Globalization;
using System.Text;

namespace pondex.Utility;

public readonly record struct LoggerDiagnostics(int Pending, long DroppedDebug, long DroppedInfo,
    long DroppedWarning, long DroppedError, long WriteFailures, bool IsDisabled);

public sealed class FileLogger : IAppLogger, IAsyncDisposable
{
    private const int DefaultQueueCapacity = 1_024;
    private const int BatchSize = 128;
    private static readonly string[] LevelNames = ["DEBUG", "INFO", "WARN", "ERROR"];
    private readonly object _sync = new();
    private readonly LinkedList<Entry> _pending = [];
    private readonly LinkedList<Entry>[] _byPriority = [[], [], [], []];
    private readonly long[] _dropped = new long[4];
    private readonly SemaphoreSlim _available = new(0, 1);
    private readonly int _capacity;
    private readonly TimeProvider _timeProvider;
    private readonly Func<DateOnly, TextWriter>? _writerFactory;
    private readonly Task _writerTask;
    private TextWriter _writer;
    private DateOnly _writerDate;
    private long _writeFailures;
    private bool _disabled;
    private bool _completing;
    private int _detailedLoggingEnabled;

    public FileLogger(string logDirectory)
        : this(date => CreateWriter(logDirectory, date), TimeProvider.System, DefaultQueueCapacity) { }

    internal FileLogger(TextWriter writer, int queueCapacity)
        : this((writer, default), null, TimeProvider.System, queueCapacity) { }

    internal FileLogger(Func<DateOnly, TextWriter> writerFactory, TimeProvider timeProvider, int queueCapacity = DefaultQueueCapacity)
        : this(CreateInitialWriter(writerFactory, timeProvider), writerFactory, timeProvider, queueCapacity) { }

    private FileLogger((TextWriter Writer, DateOnly Date) initial, Func<DateOnly, TextWriter>? writerFactory, TimeProvider timeProvider, int capacity)
    {
        ArgumentNullException.ThrowIfNull(initial.Writer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _writer = initial.Writer;
        _writerFactory = writerFactory;
        _timeProvider = timeProvider;
        _writerDate = initial.Date;
        _capacity = capacity;
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public LoggerDiagnostics Diagnostics
    {
        get
        {
            lock (_sync)
            {
                return new(_pending.Count, _dropped[0], _dropped[1], _dropped[2], _dropped[3], _writeFailures, _disabled);
            }
        }
    }

    public bool DetailedLoggingEnabled
    {
        get => Volatile.Read(ref _detailedLoggingEnabled) != 0 && !Volatile.Read(ref _disabled);
        set
        {
            if (Interlocked.Exchange(ref _detailedLoggingEnabled, value ? 1 : 0) != (value ? 1 : 0))
            {
                Info($"詳細ログ: {(value ? "ON" : "OFF")}");
            }
        }
    }

    public void Debug(string message)
    {
        if (DetailedLoggingEnabled) { Enqueue(0, message); }
    }

    public void Info(string message) => Enqueue(1, message);
    public void Warning(string message) => Enqueue(2, message);
    public void Error(string message, Exception? exception = null) =>
        Enqueue(3, exception is null ? message : $"{message} | {exception}");

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _completing = true;
            SignalWriter();
        }
        // Repeated/concurrent disposal waits for the same drain and writer disposal.
        return new ValueTask(_writerTask);
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
                    if (char.IsControl(character) || character is '\u2028' or '\u2029'
                        || char.GetUnicodeCategory(character) == UnicodeCategory.Format)
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

    private static (TextWriter, DateOnly) CreateInitialWriter(Func<DateOnly, TextWriter> factory, TimeProvider timeProvider)
    {
        var date = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        return (factory(date), date);
    }

    private static StreamWriter CreateWriter(string logDirectory, DateOnly date)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"pondex-{date:yyyyMMdd}.log");
        return new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous), new UTF8Encoding(false));
    }

    private void Enqueue(int priority, string message)
    {
        lock (_sync)
        {
            if (_disabled || _completing || (priority == 0 && !DetailedLoggingEnabled)) { return; }
            if (_pending.Count == _capacity)
            {
                // Evict the oldest record of the lowest eligible priority in O(1).
                Entry? victim = null;
                for (var level = 0; level <= priority; level++)
                {
                    if (_byPriority[level].First is { } node) { victim = node.Value; break; }
                }
                if (victim is null) { _dropped[priority]++; return; }
                Remove(victim);
                _dropped[victim.Priority]++;
            }
            // Timestamp and enqueue order are captured together, before background I/O.
            var entry = new Entry(_timeProvider.GetLocalNow(), priority, message);
            entry.OrderNode = _pending.AddLast(entry);
            entry.PriorityNode = _byPriority[priority].AddLast(entry);
            SignalWriter();
        }
    }

    private void SignalWriter()
    {
        if (_available.CurrentCount == 0) { _available.Release(); }
    }

    private void Remove(Entry entry)
    {
        _pending.Remove(entry.OrderNode!);
        _byPriority[entry.Priority].Remove(entry.PriorityNode!);
    }

    private async Task WriteLoopAsync()
    {
        var batch = new List<Entry>(BatchSize);
        var written = 0;
        long reportedDrops = 0;
        try
        {
            while (true)
            {
                await _available.WaitAsync().ConfigureAwait(false);
                bool complete;
                lock (_sync)
                {
                    while (batch.Count < BatchSize && _pending.First is { } node)
                    {
                        batch.Add(node.Value);
                        Remove(node.Value);
                    }
                    complete = _completing && _pending.Count == 0;
                    if (_pending.Count > 0) { SignalWriter(); }
                }
                written = 0;
                foreach (var entry in batch)
                {
                    await WriteRecordAsync(entry).ConfigureAwait(false);
                    written++;
                }
                var diagnostics = Diagnostics;
                var totalDrops = diagnostics.DroppedDebug + diagnostics.DroppedInfo + diagnostics.DroppedWarning + diagnostics.DroppedError;
                if (totalDrops != reportedDrops && batch.Count > 0)
                {
                    await WriteRecordAsync(new Entry(batch[^1].Timestamp, 2,
                        $"ログ破棄（累計）: DEBUG={diagnostics.DroppedDebug} INFO={diagnostics.DroppedInfo} WARN={diagnostics.DroppedWarning} ERROR={diagnostics.DroppedError}")).ConfigureAwait(false);
                    reportedDrops = totalDrops;
                }
                await _writer.FlushAsync().ConfigureAwait(false);
                batch.Clear();
                if (complete) { break; }
            }
        }
        catch
        {
            lock (_sync)
            {
                _disabled = true;
                _writeFailures++;
                foreach (var entry in batch.Skip(written)) { _dropped[entry.Priority]++; }
                foreach (var entry in _pending) { _dropped[entry.Priority]++; }
                _pending.Clear();
                foreach (var queue in _byPriority) { queue.Clear(); }
            }
        }
        finally
        {
            try { await _writer.DisposeAsync().ConfigureAwait(false); }
            catch { lock (_sync) { _writeFailures++; _disabled = true; } }
        }
    }

    private async Task WriteRecordAsync(Entry entry)
    {
        var date = DateOnly.FromDateTime(entry.Timestamp.DateTime);
        if (_writerFactory is not null && date != _writerDate)
        {
            await _writer.FlushAsync().ConfigureAwait(false);
            await _writer.DisposeAsync().ConfigureAwait(false);
            _writer = _writerFactory(date);
            _writerDate = date;
        }
        await _writer.WriteLineAsync($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{LevelNames[entry.Priority]}] {Sanitize(entry.Message)}").ConfigureAwait(false);
    }

    private sealed class Entry(DateTimeOffset timestamp, int priority, string message)
    {
        public DateTimeOffset Timestamp { get; } = timestamp;
        public int Priority { get; } = priority;
        public string Message { get; } = message;
        public LinkedListNode<Entry>? OrderNode { get; set; }
        public LinkedListNode<Entry>? PriorityNode { get; set; }
    }
}
