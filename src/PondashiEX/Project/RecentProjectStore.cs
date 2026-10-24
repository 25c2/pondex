using System.Text;
using System.Text.Json;

namespace PondashiEX.Project;

public sealed class RecentProjectStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _path;
    private readonly string? _legacyPath;
    private readonly string _portableRoot;

    public RecentProjectStore(string path, string? legacyPath = null, string? portableRoot = null)
    {
        _path = path;
        _legacyPath = legacyPath;
        _portableRoot = Path.GetFullPath(portableRoot ?? Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    public IReadOnlyList<string> Load()
    {
        try
        {
            var source = File.Exists(_path) ? _path : _legacyPath;
            if (source is null || !File.Exists(source) || new FileInfo(source).Length > 64 * 1024)
            {
                return [];
            }

            var json = File.ReadAllText(source, Utf8NoBom);
            return (JsonSerializer.Deserialize<List<string>>(json) ?? [])
                .Select(ResolveEntry)
                .OfType<string>()
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public void Add(string path)
    {
        try
        {
            var fullPath = ProjectPath.GetSafeFullPath(path, "履歴のプロジェクトパス");
            var items = Load()
                .Where(item => !string.Equals(item, fullPath, StringComparison.OrdinalIgnoreCase))
                .Prepend(fullPath)
                .Take(10)
                .ToArray();
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var tempPath = Path.Combine(directory, $".recent-{Guid.NewGuid():N}.tmp");
            var ownsTempFile = false;
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, Utf8NoBom))
                {
                    ownsTempFile = true;
                    writer.Write(JsonSerializer.Serialize(items.Select(ToStoredEntry)));
                }
                File.Move(tempPath, _path, overwrite: true);
            }
            finally
            {
                if (ownsTempFile && File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        catch
        {
            // 最近使った項目は補助機能なので、本番操作を妨げない。
        }
    }
    private string? ResolveEntry(string entry)
    {
        try
        {
            return ProjectPath.GetSafeFullPath(Path.GetFullPath(entry, _portableRoot), "履歴のプロジェクトパス");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private string ToStoredEntry(string entry)
    {
        var relative = Path.GetRelativePath(_portableRoot, entry);
        return !Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? relative : entry;
    }
}

