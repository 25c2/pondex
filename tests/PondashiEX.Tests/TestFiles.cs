namespace PondashiEX.Tests;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "PondashiEX.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string GetPath(string relativePath)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        var parent = System.IO.Path.GetDirectoryName(fullPath);
        if (parent is not null)
        {
            Directory.CreateDirectory(parent);
        }

        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

