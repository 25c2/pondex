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
}
