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

    [Theory]
    [InlineData(@"\\attacker\share\sound.wav")]
    [InlineData(@"\\?\C:\sound.wav")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData("file://attacker/share/sound.wav")]
    public void Resolve_RejectsRemoteAndDevicePaths(string unsafePath)
    {
        using var directory = new TestDirectory();
        var project = directory.GetPath("show.pondex");

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectPath.Resolve(project, unsafePath));

        Assert.Contains("使用できません", exception.Message);
    }

    [Fact]
    public void Classify_DistinguishesProjectFilesFromExternalLocalFiles()
    {
        using var directory = new TestDirectory();
        var project = directory.GetPath("show/show.pondex");

        Assert.Equal(
            ProjectPathKind.WithinProjectDirectory,
            ProjectPath.Classify(project, "sounds/bell.wav"));
        Assert.Equal(
            ProjectPathKind.ExternalLocal,
            ProjectPath.Classify(project, "../shared/bell.wav"));
    }

    [Fact]
    public void Classify_JunctionToOutsideDirectoryRequiresExternalConfirmation()
    {
        using var directory = new TestDirectory();
        var project = directory.GetPath("show/show.pondex");
        var outside = directory.GetPath("outside/sound.wav");
        File.WriteAllBytes(outside, []);
        var link = directory.GetPath("show/linked");
        TestLinks.CreateJunction(link, Path.GetDirectoryName(outside)!);
        try
        {
            Assert.Equal(ProjectPathKind.ExternalLocal, ProjectPath.Classify(project, "linked/sound.wav"));
            Assert.Equal(Path.GetFullPath(outside), ProjectPath.Resolve(project, "linked/sound.wav"));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public void Classify_JunctionInsideProjectRemainsInternal()
    {
        using var directory = new TestDirectory();
        var project = directory.GetPath("show.pondex");
        var sound = directory.GetPath("audio/sound.wav");
        File.WriteAllBytes(sound, []);
        var link = directory.GetPath("linked");
        TestLinks.CreateJunction(link, Path.GetDirectoryName(sound)!);
        try
        {
            Assert.Equal(ProjectPathKind.WithinProjectDirectory, ProjectPath.Classify(project, "linked/sound.wav"));
        }
        finally
        {
            Directory.Delete(link);
        }
    }
}
