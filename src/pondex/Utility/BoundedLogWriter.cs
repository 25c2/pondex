using System.Globalization;
using System.Text;

namespace pondex.Utility;

internal sealed class BoundedLogWriter : TextWriter
{
    private const int MaxFileBytes = 4 * 1024 * 1024;
    private const int MaxTotalBytes = 32 * 1024 * 1024;
    private const int KeepDays = 14;
    private const int MaxRecordChars = 16 * 1024;
    private readonly string _directory;
    private readonly DateOnly _date;
    private readonly string _stem;
    private StreamWriter _stream;
    private long _bytes;
    private int _part;

    public BoundedLogWriter(string directory, DateOnly date)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _date = date;
        _stem = "pondex-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        Directory.CreateDirectory(directory);

        _part = ListLogs().Where(log => log.Date == date).Select(log => log.Part).DefaultIfEmpty(0).Max();
        _stream = OpenPart();
    }

    public override Encoding Encoding { get; } = new UTF8Encoding(false);

    public override async Task WriteLineAsync(string? value)
    {
        var record = value ?? string.Empty;
        if (record.Length > MaxRecordChars) { record = record[..MaxRecordChars] + " …[省略]"; }
        var bytes = Encoding.GetByteCount(record) + Encoding.GetByteCount(_stream.NewLine);
        if (_bytes + bytes > MaxFileBytes)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _part++;
            _stream = OpenPart();
        }
        await _stream.WriteLineAsync(record).ConfigureAwait(false);
        _bytes += bytes;
    }

    public override Task FlushAsync() => _stream.FlushAsync();

    public override async ValueTask DisposeAsync()
    {
        try { await _stream.DisposeAsync().ConfigureAwait(false); }
        finally { await base.DisposeAsync().ConfigureAwait(false); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _stream.Dispose(); }
        base.Dispose(disposing);
    }

    private StreamWriter OpenPart()
    {
        var name = _part == 0 ? $"{_stem}.log" : string.Create(CultureInfo.InvariantCulture, $"{_stem}-{_part:D4}.log");
        var path = Path.Combine(_directory, name);
        // Do not append through links that happen to have a recognized filename.
        if (new FileInfo(path).LinkTarget is not null)
        {
            throw new IOException($"ログファイルがリンクです: {name}");
        }

        var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous);
        _bytes = file.Length;
        Prune(name);
        return new StreamWriter(file, Encoding);
    }

    private void Prune(string activeName)
    {
        try
        {
            if (new DirectoryInfo(_directory).Attributes.HasFlag(FileAttributes.ReparsePoint)) { return; }

            var files = ListLogs().Where(log => log.File.Name != activeName)
                .OrderBy(log => log.Date).ThenBy(log => log.Part).ToArray();
            var bytes = files.Sum(log => log.File.Length);
            foreach (var (file, date, _) in files)
            {
                if (date >= _date.AddDays(1 - KeepDays) && bytes <= MaxTotalBytes - MaxFileBytes) { break; }
                try
                {
                    var length = file.Length;
                    file.Refresh();
                    if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) { continue; }
                    file.Delete();
                    bytes -= length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Retention is best effort when a file is in use or storage is unavailable.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failure to prune old files must not interrupt logging or playback.
        }
    }

    private IEnumerable<(FileInfo File, DateOnly Date, int Part)> ListLogs()
    {
        foreach (var file in new DirectoryInfo(_directory).EnumerateFiles("pondex-*.log"))
        {
            var name = file.Name.AsSpan();
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint) || name.Length < 19
                || !name.StartsWith("pondex-", StringComparison.Ordinal) || !name.EndsWith(".log", StringComparison.Ordinal)
                || !DateOnly.TryParseExact(name.Slice(7, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            var part = 0;
            if (name.Length != 19)
            {
                if (name[15] != '-' || name.Length < 24 || !int.TryParse(name[16..^4], NumberStyles.None, CultureInfo.InvariantCulture, out part)
                    || part <= 0 || !name[16..^4].SequenceEqual(part.ToString("D4", CultureInfo.InvariantCulture))) { continue; }
            }

            yield return (file, date, part);
        }
    }
}
