using System.Text;
using System.Text.Json;

namespace PondashiEX.Project;

public sealed class RecentProjectStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _path;

    public RecentProjectStore(string path)
    {
        _path = path;
    }

    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var json = File.ReadAllText(_path, Utf8NoBom);
            return (JsonSerializer.Deserialize<List<string>>(json) ?? [])
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
            var fullPath = Path.GetFullPath(path);
            var items = Load()
                .Where(item => !string.Equals(item, fullPath, StringComparison.OrdinalIgnoreCase))
                .Prepend(fullPath)
                .Take(10)
                .ToArray();
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(items), Utf8NoBom);
            File.Move(tempPath, _path, overwrite: true);
        }
        catch
        {
            // 最近使った項目は補助機能なので、本番操作を妨げない。
        }
    }
}

