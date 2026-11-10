namespace pondex.Utility;

internal sealed record AppStoragePaths(string DataDirectory, string LogDirectory, bool IsPortable, bool CanWrite)
{
    // Only read the previous application's data; never rename or delete it.
    public static string GetLegacyHistoryPath(string localApplicationData) =>
        Path.Combine(localApplicationData, "PondashiEX", "recent-projects.json");

    public static AppStoragePaths Resolve(
        string applicationDirectory, string userDirectory, string temporaryDirectory,
        Func<string, bool>? canWrite = null)
    {
        canWrite ??= ProbeDirectory;
        var data = Path.Combine(applicationDirectory, "Data");
        var logs = Path.Combine(applicationDirectory, "Logs");
        if (canWrite(data) && canWrite(logs))
        {
            return new(data, logs, IsPortable: true, CanWrite: true);
        }
        logs = Path.Combine(userDirectory, "Logs");
        if (canWrite(userDirectory) && canWrite(logs))
        {
            return new(userDirectory, logs, IsPortable: false, CanWrite: true);
        }
        var temporary = Path.Combine(temporaryDirectory, "pondex", Guid.NewGuid().ToString("N"));
        logs = Path.Combine(temporary, "Logs");
        var writable = canWrite(temporary) && canWrite(logs);
        return new(temporary, logs, IsPortable: false, CanWrite: writable);
    }

    private static bool ProbeDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1, FileOptions.DeleteOnClose | FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
