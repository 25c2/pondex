using PondashiEX.Project;

namespace PondashiEX.Tests;

public sealed class ProjectManagerTests
{
    [Fact]
    public async Task SaveAsync_ClearsDirtyStateAndNotifiesHeaderConsumers()
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
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
        using var manager = new ProjectManager(new ProjectSerializer());
        await manager.SaveAsAsync(projectPath);
        var sound = manager.AddSound(directory.GetPath("old.wav"));

        manager.ReplaceSoundFile(sound, audioPath);

        Assert.Equal("audio/replacement.wav", sound.File);
        Assert.True(manager.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_ValidatesSnapshotWithoutMutatingLiveModel()
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var projectPath = directory.GetPath("show.pondex");
        var audioPath = directory.GetPath("sound.wav");
        await File.WriteAllBytesAsync(audioPath, []);
        await manager.SaveAsAsync(projectPath);
        var sound = manager.AddSound(audioPath);
        sound.Volume = 2.0f;
        manager.MarkDirty();

        await manager.SaveAsync();

        Assert.Equal(2.0f, sound.Volume);
        var saved = await new ProjectSerializer().LoadAsync(projectPath);
        Assert.Equal(1.0f, Assert.Single(saved.Sounds).Volume);
    }
}
