using PondashiEX.Project;

namespace PondashiEX.Tests;

public sealed class ProjectManagerTests
{
    [Fact]
    public async Task SaveAsync_ClearsDirtyStateAndNotifiesHeaderConsumers()
    {
        using var directory = new TestDirectory();
        var manager = new ProjectManager(new ProjectSerializer());
        var path = directory.GetPath("saved.pondex");
        await manager.SaveAsAsync(path);
        manager.MarkDirty();
        var notifications = 0;
        manager.ProjectChanged += (_, _) => notifications++;

        await manager.SaveAsync();

        Assert.False(manager.IsDirty);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task ReplaceSoundFile_StoresPathRelativeToProject()
    {
        using var directory = new TestDirectory();
        var projectPath = directory.GetPath("show.pondex");
        var audioPath = directory.GetPath(Path.Combine("audio", "replacement.wav"));
        Directory.CreateDirectory(Path.GetDirectoryName(audioPath)!);
        await File.WriteAllBytesAsync(audioPath, []);
        var manager = new ProjectManager(new ProjectSerializer());
        await manager.SaveAsAsync(projectPath);
        var sound = manager.AddSound(directory.GetPath("old.wav"));

        manager.ReplaceSoundFile(sound, audioPath);

        Assert.Equal("audio/replacement.wav", sound.File);
        Assert.True(manager.IsDirty);
    }
}
