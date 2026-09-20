using PondashiEX.Project;

namespace PondashiEX.Tests;

public sealed class ProjectPathTests
{
    [Fact]
    public void RelativePath_UsesForwardSlashesAndSurvivesFolderMove()
    {
        using var directory = new TestDirectory();
        var originalProject = directory.GetPath("original/show.pondex");
        var audio = directory.GetPath("original/sounds/bell.wav");
        File.WriteAllBytes(audio, []);

        var stored = ProjectPath.ToStoredPath(originalProject, audio);
        var movedProject = directory.GetPath("moved/show.pondex");
        var resolved = ProjectPath.Resolve(movedProject, stored);

        Assert.Equal("sounds/bell.wav", stored);
        var expected = Path.Combine(directory.Path, "moved", "sounds", "bell.wav");
        Assert.Equal(expected, resolved);
    }
}
