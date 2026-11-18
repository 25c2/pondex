using pondex.Utility;

namespace pondex.Tests;

public sealed class BoundedLogWriterTests
{
    [Fact]
    public async Task Retention_RotatesFullFileAndDeletesOnlyKnownLogs()
    {
        using var directory = new TestDirectory();
        var oldLog = directory.GetPath("pondex-20261001-0001.log");
        var similarName = directory.GetPath("pondex-20261001-copy.log");
        var backup = directory.GetPath("show.pondex.bak");
        File.WriteAllText(oldLog, "old");
        File.WriteAllText(similarName, "keep");
        File.WriteAllText(backup, "keep");

        const int maxFile = 4 * 1024 * 1024;
        var fullLog = directory.GetPath("pondex-20261118.log");
        using (var file = File.Create(fullLog)) { file.SetLength(maxFile); }
        await using (var writer = new BoundedLogWriter(directory.Path, new DateOnly(2026, 11, 18)))
        {
            await writer.WriteLineAsync("new");
        }

        Assert.False(File.Exists(oldLog));
        Assert.Equal("keep", File.ReadAllText(similarName));
        Assert.Equal("keep", File.ReadAllText(backup));
        Assert.Equal(maxFile, new FileInfo(fullLog).Length);
        Assert.Equal("new", Assert.Single(File.ReadAllLines(directory.GetPath("pondex-20261118-0001.log"))));
    }

    [Fact]
    public void Retention_PrunesOldestSegmentsToMakeRoomForTheActiveFile()
    {
        using var directory = new TestDirectory();
        const int maxFile = 4 * 1024 * 1024;
        for (var part = 1; part <= 8; part++)
        {
            using var file = File.Create(directory.GetPath($"pondex-20261117-{part:D4}.log"));
            file.SetLength(maxFile);
        }

        using var writer = new BoundedLogWriter(directory.Path, new DateOnly(2026, 11, 18));
        Assert.False(File.Exists(directory.GetPath("pondex-20261117-0001.log")));
        Assert.True(File.Exists(directory.GetPath("pondex-20261117-0002.log")));
        Assert.True(Directory.GetFiles(directory.Path, "*.log").Sum(path => new FileInfo(path).Length) <= 32L * 1024 * 1024);
    }
}
