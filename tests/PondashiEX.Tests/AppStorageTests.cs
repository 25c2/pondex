using System.Text.Json;
using PondashiEX.Project;
using PondashiEX.Utility;

namespace PondashiEX.Tests;

public sealed class AppStorageTests
{
    [Fact]
    public void WritableApplicationFolderIsPreferredAndProbeFilesAreRemoved()
    {
        using var directory = new TestDirectory();
        var storage = AppStoragePaths.Resolve(directory.GetPath("app"), directory.GetPath("user"), directory.GetPath("temp"));
        Assert.True(storage.IsPortable);
        Assert.True(storage.CanWrite);
        Assert.Equal(Path.GetFullPath(directory.GetPath("app/Data")), storage.DataDirectory);
        Assert.Empty(Directory.GetFiles(directory.Path, ".write-probe-*", SearchOption.AllDirectories));
    }

    [Fact]
    public void ReadOnlyApplicationFolderFallsBackToUserThenTemporaryWithoutThrowing()
    {
        using var directory = new TestDirectory();
        var app = directory.GetPath("app");
        var user = directory.GetPath("user");
        var temp = directory.GetPath("temp");
        var userStorage = AppStoragePaths.Resolve(app, user, temp, path => path.StartsWith(user, StringComparison.Ordinal));
        Assert.False(userStorage.IsPortable);
        Assert.True(userStorage.CanWrite);
        Assert.Equal(user, userStorage.DataDirectory);
        var temporary = AppStoragePaths.Resolve(app, user, temp, path => path.StartsWith(temp, StringComparison.Ordinal));
        Assert.True(temporary.CanWrite);
        Assert.StartsWith(temp, temporary.DataDirectory);
        Assert.False(AppStoragePaths.Resolve(app, user, temp, _ => false).CanWrite);
    }

    [Fact]
    public void LegacyHistoryIsReadAndMigratedWithoutChangingTheOldFile()
    {
        using var directory = new TestDirectory();
        var project = directory.GetPath("app/projects/show.pondex");
        File.WriteAllText(project, "{}");
        var legacy = directory.GetPath("old/recent-projects.json");
        var original = JsonSerializer.Serialize(new[] { project });
        File.WriteAllText(legacy, original);
        var destination = directory.GetPath("app/Data/recent-projects.json");
        var store = new RecentProjectStore(destination, legacy, directory.GetPath("app"));
        Assert.Equal(Path.GetFullPath(project), Assert.Single(store.Load()));
        store.Add(project);
        Assert.Equal(original, File.ReadAllText(legacy));
        Assert.Equal(Path.GetFullPath(project), Assert.Single(store.Load()));
        Assert.DoesNotContain(directory.Path, File.ReadAllText(destination));
    }

    [Fact]
    public void CopiedPortableHistoryResolvesProjectsOnTheNewDriveRoot()
    {
        using var directory = new TestDirectory();
        var originalProject = directory.GetPath("original/projects/show.pondex");
        File.WriteAllText(originalProject, "{}");
        var originalHistory = directory.GetPath("original/Data/recent-projects.json");
        new RecentProjectStore(originalHistory, portableRoot: directory.GetPath("original")).Add(originalProject);
        var movedProject = directory.GetPath("copied/projects/show.pondex");
        File.Copy(originalProject, movedProject);
        var movedHistory = directory.GetPath("copied/Data/recent-projects.json");
        File.Copy(originalHistory, movedHistory);
        Assert.Equal(Path.GetFullPath(movedProject), Assert.Single(new RecentProjectStore(movedHistory, portableRoot: directory.GetPath("copied")).Load()));
    }

    [Fact]
    public void UnwritableHistoryDoesNotThrowOrDeleteLegacyData()
    {
        using var directory = new TestDirectory();
        var project = directory.GetPath("show.pondex");
        File.WriteAllText(project, "{}");
        var legacy = directory.GetPath("old.json");
        File.WriteAllText(legacy, JsonSerializer.Serialize(new[] { project }));
        var blocker = directory.GetPath("not-a-directory");
        File.WriteAllText(blocker, "keep");
        var store = new RecentProjectStore(Path.Combine(blocker, "recent.json"), legacy);
        store.Add(project);
        Assert.Equal("keep", File.ReadAllText(blocker));
        Assert.Equal(project, Assert.Single(store.Load()));
    }
}
