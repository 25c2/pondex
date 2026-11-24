using System.Text.Json;

namespace pondex.Utility;

internal sealed class UiPreferences(string directory)
{
    internal int? HeaderHeight
    {
        get => Read<int>("header-height.json") is >= 1 and <= 4096 and var height ? height : null;
    }

    internal bool UseLocalLogs => Read<bool>("local-logs.json") == true;

    internal void SaveHeaderHeight(int? value) => Write("header-height.json", value);

    internal void SaveUseLocalLogs(bool value) => Write("local-logs.json", value);

    private T? Read<T>(string name) where T : struct
    {
        try
        {
            using var file = File.OpenRead(Path.Combine(directory, name));
            return file.Length <= 1024 ? JsonSerializer.Deserialize<T?>(file) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".ui-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, value);
            }
            File.Move(temporary, Path.Combine(directory, name), overwrite: true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
}

internal static class HeaderHeight
{
    internal static int Clamp(int? requested, int minimum, int clientHeight, int reservedHeight, int dpi)
    {
        var pixels = requested is { } height ? Scale(height, dpi) : minimum;
        return Math.Clamp(pixels, minimum, Math.Max(minimum, clientHeight - reservedHeight));
    }

    internal static int Scale(int value, int dpi) => (int)Math.Round(value * (dpi / 96d));
}
