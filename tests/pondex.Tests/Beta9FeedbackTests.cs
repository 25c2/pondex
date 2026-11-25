using NAudio.Wave;
using pondex.Audio;
using pondex.Project;
using pondex.Utility;
using pondex.Trigger;

namespace pondex.Tests;

public sealed class Beta9FeedbackTests
{
    private static readonly string[] UnreadableNames = ["changed", "empty", "missing"];
    private static readonly string[] UndecodableNames = ["non-audio", "changed", "empty", "missing"];

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
    public async Task Preflight_SeparatesReadingFromDecodingAndIdentifiesFailures()
    {
        using var directory = new TestDirectory();
        var wave = directory.GetPath("valid.wav");
        using (var writer = new WaveFileWriter(wave, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
        {
            writer.WriteSample(0.05F);
        }
        var readable = directory.GetPath("readable.wav");
        var empty = directory.GetPath("empty.wav");
        await File.WriteAllBytesAsync(readable, [10, 20, 30]);
        await File.WriteAllBytesAsync(empty, []);
        (string Name, PlaybackPath Path)[] sources =
        [
            ("valid", new(wave, null, wave)),
            ("non-audio", new(readable, null, readable)),
            ("changed", new(readable, null, empty)),
            ("empty", new(empty, null, empty)),
            ("missing", new(directory.GetPath("missing.wav"), null, null))
        ];
        var progress = new InlineProgress();
        var read = await SoundPreflight.CheckAsync(sources, SoundCheckMode.Read, progress, CancellationToken.None);
        Assert.Equal(UnreadableNames, read.Failures.Select(failure => failure.Name));
        Assert.Contains("空", read.Failures[1].Reason);
        Assert.Contains("見つかりません", read.Failures[2].Reason);
        Assert.Equal(sources.Length, progress.Last);

        var decode = await SoundPreflight.CheckAsync(sources, SoundCheckMode.Decode, null, CancellationToken.None);
        Assert.Equal(UndecodableNames, decode.Failures.Select(failure => failure.Name));
        Assert.Equal(new byte[] { 10, 20, 30 }, await File.ReadAllBytesAsync(readable));
    }

    [Fact]
    public async Task CancellingPreflight_DoesNotReturnResultsBeforeRunningCheckExits()
    {
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var check = SoundPreflight.CheckAsync([("blocked", default)], SoundCheckMode.Decode, null, cancellation.Token,
            async (_, _) =>
            {
                active.SetResult();
                await release.Task;
            });
        await active.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        Assert.False(check.IsCompleted);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await check);
    }

    [Fact]
    public async Task SaveAs_RejectsChangedOrNewlyCreatedDestinationAfterApproval()
    {
        using var directory = new TestDirectory();
        var target = directory.GetPath("existing.pondex");
        var newTarget = directory.GetPath("new.pondex");
        var serializer = new ProjectSerializer();
        using var manager = new ProjectManager(serializer);
        await serializer.SaveAsync(target, new ProjectModel { Project = new ProjectMetadata { Name = "old" } });
        var approvedOld = await ProjectSaveDestination.CaptureAsync(target);
        var approvedNew = await ProjectSaveDestination.CaptureAsync(newTarget);
        await serializer.SaveAsync(target, new ProjectModel { Project = new ProjectMetadata { Name = "other window" } });
        await serializer.SaveAsync(newTarget, new ProjectModel { Project = new ProjectMetadata { Name = "new owner" } });

        await Assert.ThrowsAsync<IOException>(() => manager.SaveAsAsync(approvedOld, null));
        await Assert.ThrowsAsync<IOException>(() => manager.SaveAsAsync(approvedNew, null));
        Assert.Equal("other window", (await serializer.LoadAsync(target)).Project.Name);
        Assert.Equal("new owner", (await serializer.LoadAsync(newTarget)).Project.Name);
        Assert.Null(manager.CurrentPath);

        await manager.SaveAsAsync(await ProjectSaveDestination.CaptureAsync(target), null);
        Assert.Equal(target, manager.CurrentPath);
    }

    [Fact]
    public async Task HeldGroupEvent_ReloadsWithoutWarningsAndNeedsExplicitRepair()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("show.pondex");
        var serializer = new ProjectSerializer();
        var source = new ProjectModel();
        source.Triggers.Add(new TriggerDefinition
        {
            Enabled = true,
            TargetGroup = "old",
            TargetGroupOnHold = true,
            TriggerType = TriggerType.StopwatchElapsed,
            Parameters = new TriggerParameters { Elapsed = TimeSpan.Zero }
        });
        await serializer.SaveAsync(path, source);
        using var manager = new ProjectManager(serializer);
        Assert.True(await manager.OpenAsync(path, (_, _) => true));
        Assert.False(manager.RequiresSaveAs);
        var trigger = Assert.Single(manager.Current.Triggers);
        Assert.True(trigger.TargetGroupOnHold);
        Assert.False(trigger.Enabled);
        var runtime = new TriggerEngine();
        var now = DateTimeOffset.Now;
        trigger.Enabled = true;
        runtime.SetTriggers(manager.Current.Triggers);
        Assert.Empty(runtime.Tick(now, TimeSpan.Zero, true));

        trigger.TargetGroup = "new";
        trigger.TargetGroupOnHold = false;
        manager.Current.Sounds.Add(new SoundDefinition { Group = "new", File = "cue.wav" });
        manager.MarkDirty();
        runtime.SetTriggers(manager.Current.Triggers);
        Assert.Single(runtime.Tick(now, TimeSpan.Zero, true));
        await manager.SaveAsync();
        Assert.False((await serializer.LoadAsync(path)).Triggers[0].TargetGroupOnHold);
    }

    [Fact]
    public async Task HeaderPreferenceQueue_PersistsLatestHeightAndReset()
    {
        using var directory = new TestDirectory();
        var preferences = new UiPreferences(directory.Path);
        static void Fail(Exception error) => throw new InvalidOperationException("UI preference write failed", error);
        preferences.QueueHeaderHeight(190, Fail);
        preferences.QueueHeaderHeight(200, Fail);
        await preferences.PendingHeaderWrite.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(200, preferences.HeaderHeight);
        preferences.QueueHeaderHeight(null, Fail);
        await preferences.PendingHeaderWrite.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(preferences.HeaderHeight);
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
