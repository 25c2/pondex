using pondex.Audio;
using pondex.Project;
using pondex.Utility;

namespace pondex.Tests;

public sealed class Beta9FeedbackTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void HeaderHeight_ClampsLogicalPreferenceToLayoutAndKeepsRoomForTabs(int dpi)
    {
        var minimum = HeaderHeight.Scale(152, dpi);
        var reserved = HeaderHeight.Scale(150, dpi);
        var window = HeaderHeight.Scale(420, dpi);
        Assert.Equal(minimum, HeaderHeight.Clamp(null, minimum, window, reserved, dpi));
        Assert.Equal(minimum, HeaderHeight.Clamp(30, minimum, window, reserved, dpi));
        Assert.Equal(HeaderHeight.Scale(220, dpi), HeaderHeight.Clamp(220, minimum, window, reserved, dpi));
        Assert.Equal(window - reserved, HeaderHeight.Clamp(2000, minimum, window, reserved, dpi));
        Assert.Equal(minimum, HeaderHeight.Clamp(220, minimum, 1, reserved, dpi));
    }

    [Fact]
    public void UiPreferences_ReloadsCorruptionAndResetIndependentlyOfLogChoice()
    {
        using var directory = new TestDirectory();
        var preferences = new UiPreferences(directory.Path);
        Assert.Null(preferences.HeaderHeight);
        Assert.False(preferences.UseLocalLogs);
        preferences.SaveHeaderHeight(192);
        preferences.SaveUseLocalLogs(true);
        Assert.Equal(192, new UiPreferences(directory.Path).HeaderHeight);
        Assert.True(new UiPreferences(directory.Path).UseLocalLogs);
        preferences.SaveHeaderHeight(null);
        Assert.Null(new UiPreferences(directory.Path).HeaderHeight);
        File.WriteAllText(directory.GetPath("header-height.json"), "corrupt");
        Assert.Null(new UiPreferences(directory.Path).HeaderHeight);
        Assert.True(new UiPreferences(directory.Path).UseLocalLogs);
    }

    [Fact]
    public async Task SaveTiming_RecordsNewAndReplacementPhasesAndPreservesBackup()
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var path = directory.GetPath("stage.pondex");
        var soundPath = directory.GetPath("source.wav");
        await File.WriteAllBytesAsync(soundPath, [1, 2, 3]);
        manager.AddSound(soundPath);

        var first = new OperationTiming();
        await manager.SaveAsAsync(path, first);
        var original = await File.ReadAllBytesAsync(path);
        Assert.Equal("new", first.Destination);
        Assert.Equal(original.Length, first.Bytes);
        var firstLine = first.Format();
        foreach (var phase in new[] { "snapshot", "destination_path", "rebase_paths", "serialize", "temp_open", "temp_write", "flush", "readback", "commit" })
        {
            Assert.Contains($" {phase}_ms=", firstLine);
        }

        manager.Current.Project.Name = "Changed";
        manager.MarkDirty();
        var second = new OperationTiming();
        await manager.SaveAsync(second);
        Assert.False(manager.IsDirty);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("replace", second.Destination);
        Assert.Equal(new FileInfo(path).Length, second.Bytes);
        Assert.Equal(original, await File.ReadAllBytesAsync(path + ".bak"));
        Assert.Contains(" revision_check_ms=", second.Format());
        Assert.Contains(" backup_ms=", second.Format());
    }

    [Fact]
    public void RecentProjects_AlreadyFirstDoesNotRewriteHistory()
    {
        using var directory = new TestDirectory();
        var history = directory.GetPath("recent-projects.json");
        var project = directory.GetPath("stage.pondex");
        File.WriteAllText(project, "{}");
        var store = new RecentProjectStore(history);
        store.Add(project);
        File.SetLastWriteTimeUtc(history, new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var before = File.GetLastWriteTimeUtc(history);
        store.Add(project);
        Assert.Equal(before, File.GetLastWriteTimeUtc(history));
        Assert.Equal(project, Assert.Single(store.Load()));
    }

    [Fact]
    public void Preflight_UsesApprovedPathAndReportsMissingEmptyOrCancelled()
    {
        using var directory = new TestDirectory();
        var readable = directory.GetPath("readable.wav");
        var empty = directory.GetPath("empty.wav");
        File.WriteAllBytes(readable, [10, 20, 30]);
        File.WriteAllBytes(empty, []);
        PlaybackPath[] paths =
        [
            new(readable, null, readable),
            new(readable, null, empty),
            new(empty, null, empty),
            new(directory.GetPath("missing.wav"), null, null)
        ];
        var progress = new InlineProgress();
        Assert.Equal(3, SoundPreflight.Check(paths, progress, CancellationToken.None));
        Assert.Equal(paths.Length, progress.Last);
        Assert.Equal(new byte[] { 10, 20, 30 }, File.ReadAllBytes(readable));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SoundPreflight.Check(paths, null, cancellation.Token));
    }

    [Fact]
    public void OutputSnapshotChanges_ReportsEndpointReplacementAndDefaultTransition()
    {
        var usb = new AudioDeviceInfo(0, "USB", "usb") { EndpointId = "first" };
        var speakers = new AudioDeviceInfo(1, "Speakers", "speakers") { EndpointId = "second" };
        var before = new AudioDeviceSnapshot([AudioDeviceInfo.Default, usb, speakers], new(0, "first"));
        Assert.False(AudioDeviceSnapshotChanges.Differ(before, new([speakers, usb, AudioDeviceInfo.Default], before.DefaultOutput)));

        var after = new AudioDeviceSnapshot([AudioDeviceInfo.Default, speakers], new(1, "second"));
        Assert.True(AudioDeviceSnapshotChanges.Differ(before, after));
        var detail = AudioDeviceSnapshotChanges.Describe(before, after);
        Assert.Contains("default_before=0:first default_after=1:second", detail);
        Assert.Contains("removed=[0:waveout-endpoint:first (usb)]", detail);
        Assert.Null(AudioOutputBinding.Create(usb.SelectionId, after));
    }

    private sealed class InlineProgress : IProgress<int>
    {
        internal int Last { get; private set; }
        public void Report(int value) => Last = value;
    }
}
