using System.Globalization;
using System.Text;

namespace pondex.Diagnostics;

internal static class DiagnosticReport
{
    internal static string Write(string directory, string prefix, string content)
    {
        Directory.CreateDirectory(directory);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, $"{prefix}-{timestamp}-{Guid.NewGuid():N}.txt");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
        return path;
    }
}
