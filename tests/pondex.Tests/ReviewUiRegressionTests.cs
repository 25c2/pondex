using System.Drawing;
using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;
using NAudio.Wave;
using pondex.Audio;
using pondex.Input;
using pondex.Project;
using pondex.Trigger;
using pondex.UI;
using pondex.Utility;

namespace pondex.Tests;

[Collection(WinFormsTestGroup.Name)]
public sealed class ReviewUiRegressionTests(ITestOutputHelper output)
{
    private static readonly bool[] ResizeMeasurementOrder = [true, false, false, true, true, false];
    private static readonly bool[] LargeResizeMeasurementOrder = [true, false];
    private static readonly AudioDeviceSnapshot Initial = new(
        [AudioDeviceInfo.Default, new AudioDeviceInfo(0, "Console", "console"), new AudioDeviceInfo(1, "Speakers", "speakers")], new(0, "console-endpoint"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeaderGrip_DragClampsKeepsControlCentersAndRestoresWithoutEditingProject(bool dark) => WinFormsTest.Run(() =>
    {
        using var directory = new TestDirectory();
        var preferences = new UiPreferences(directory.Path);
        using var manager = new ProjectManager(new ProjectSerializer());
        manager.Current.Settings.DarkMode = dark;
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output());
        MainForm Create() => new(manager, engine, new AudioDeviceManager(() => Initial), new TriggerEngine(), new Logger(),
            directory.Path, null, preferences);
        int defaultHeight;
        int grownHeight;
        using (var form = Create())
        {
            Initialize(form);
            var grip = Descendants(form).OfType<HeaderResizeGrip>().Single();
            var root = (TableLayoutPanel)grip.Parent!;
            var header = (TableLayoutPanel)root.GetControlFromPosition(0, 1)!;
            var tabs = (TabControl)root.GetControlFromPosition(0, 3)!;
            defaultHeight = header.Height;
            var master = Descendants(header).OfType<TrackBar>().Single();
            var controls = master.Parent!.Controls.Cast<Control>().ToArray();
            var centers = controls.Select(control => control.Top + control.Height / 2).ToArray();
            Assert.InRange(centers.Max() - centers.Min(), 0, 2);

            void Mouse(string method, int y) => typeof(HeaderResizeGrip).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(grip, [new MouseEventArgs(MouseButtons.Left, 1, 5, y, 0)]);
            Mouse("OnMouseDown", 2);
            Mouse("OnMouseMove", 2000);
            Mouse("OnMouseUp", 2);
            Application.DoEvents();
            grownHeight = header.Height;
            Assert.True(grownHeight > defaultHeight);
            var minimumTabsHeight = HeaderHeight.Scale(112, root.DeviceDpi);
            Assert.True(tabs.Height >= minimumTabsHeight,
                $"Tab height {tabs.Height}px is below {minimumTabsHeight}px at {root.DeviceDpi} DPI.");
            Pump(preferences.PendingHeaderWrite);
            Assert.True(preferences.HeaderHeight.HasValue);
            Assert.False(manager.IsDirty);
            form.CloseWithoutSavePromptForSmokeTest();
        }

        using var restored = Create();
        Initialize(restored);
        var restoredGrip = Descendants(restored).OfType<HeaderResizeGrip>().Single();
        var restoredRoot = (TableLayoutPanel)restoredGrip.Parent!;
        var restoredHeader = (TableLayoutPanel)restoredRoot.GetControlFromPosition(0, 1)!;
        Assert.InRange(restoredHeader.Height, grownHeight - 2, grownHeight + 2);
        typeof(HeaderResizeGrip).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(restoredGrip, [new KeyEventArgs(Keys.Home)]);
        Pump(preferences.PendingHeaderWrite);
        Assert.Null(preferences.HeaderHeight);
        Assert.InRange(restoredHeader.Height, defaultHeight - 2, defaultHeight + 2);
        Assert.False(manager.IsDirty);
        restored.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void EmergencyShortcut_LeavesUiResponsiveWhileOutputStopWaits() => WinFormsTest.Run(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var trigger = new TriggerEngine();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var engine = new AudioEngine(new Logger(), (_, _) => new BlockingStopOutput(entered, release));
        using var form = CreateForm(manager, engine, trigger, () => Initial, directory.Path);
        Initialize(form);
        var sound = new SoundDefinition();
        var play = engine.StartPlaybackAsync(sound, CreateWave(directory));
        Pump(play);
        Assert.Equal(PlaybackStartResult.Started, play.GetAwaiter().GetResult());
        Assert.Contains(sound.Id, engine.GetActiveSoundIds());

        var handled = (bool)Method("HandleWindowShortcut").Invoke(form, [Keys.Control | Keys.Shift | Keys.R, true, false])!;
        var task = (Task)Field("_manualEmergencyStopTask").GetValue(form)!;
        try
        {
            Assert.True(handled);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(task.IsCompleted);
            Assert.True(trigger.IsSuspended);
            var dispatched = false;
            form.BeginInvoke(() => dispatched = true);
            Application.DoEvents();
            Assert.True(dispatched);
        }
        finally { release.Set(); }
        Pump(task);
        Assert.Empty(engine.GetActiveSoundIds());
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void ClockTimerGap_SuspendsTriggersAndReportsUiInterruption()
        => WinFormsTest.Run(() =>
        {
            using var directory = new TestDirectory();
            using var manager = new ProjectManager(new ProjectSerializer());
            var trigger = new TriggerEngine();
            using var engine = new AudioEngine(new Logger(), (_, _) => new Output());
            using var form = CreateForm(manager, engine, trigger, () => Initial, directory.Path);
            Initialize(form);
            Field("_previousClockTick").SetValue(form,
                (DateTimeOffset.Now.AddSeconds(-6), Stopwatch.GetTimestamp() - 6 * Stopwatch.Frequency));
            Method("UpdateClockAndTriggers").Invoke(form, null);
            Assert.True(trigger.IsSuspended);
            var status = (ToolStripStatusLabel)Field("_statusLabel").GetValue(form)!;
            Assert.Contains("画面の処理が長時間止まったため", status.Text);
            form.CloseWithoutSavePromptForSmokeTest();
        });

    [Fact]
    public void OutputChoices_DistinguishIdenticallyNamedDevices() => WinFormsTest.Run(() =>
    {
        using var dialog = new SettingsDialog(new ProjectSettings(),
            [AudioDeviceInfo.Default, new AudioDeviceInfo(0, "USB", "first"), new AudioDeviceInfo(2, "USB", "second")]);
        dialog.Show();
        var combo = Descendants(dialog).OfType<ComboBox>().Single();
        Assert.Contains(combo.Items.Cast<object>(), item => combo.GetItemText(item) == "USB（番号 0）");
        Assert.Contains(combo.Items.Cast<object>(), item => combo.GetItemText(item) == "USB（番号 2）");
        combo.SelectedValue = 2;
        Assert.Equal(2, dialog.SelectedDeviceNumber);
    });

    [Fact]
    public void OutputRefresh_RemainsResponsiveAndClearsSelectionAfterHotplug() => WinFormsTest.Run(() =>
    {
        var refresh = new TaskCompletionSource<AudioDeviceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = false;
        using var dialog = new SettingsDialog(new ProjectSettings { OutputDevice = "console" }, Initial.Devices);
        dialog.ConfigureOutputRefresh(Initial, () => refresh.Task, () => stopped = true);
        dialog.Show();
        var refreshButton = Descendants(dialog).OfType<Button>().Single(button => button.Text == "更新");
        var emergency = Descendants(dialog).OfType<Button>().Single(button => button.Text == "緊急停止");
        refreshButton.PerformClick();
        Assert.False(refreshButton.Enabled);
        emergency.PerformClick();
        Assert.True(stopped);
        var changed = Initial with { Devices = [AudioDeviceInfo.Default, Initial.Devices[2]], DefaultOutput = new(1, "speakers-endpoint") };
        refresh.SetResult(changed);
        var deadline = Stopwatch.StartNew();
        while (!refreshButton.Enabled && deadline.Elapsed < TimeSpan.FromSeconds(5)) { Application.DoEvents(); }
        Assert.True(refreshButton.Enabled);
        Assert.Equal(changed, dialog.OutputSnapshot);
        var combo = Descendants(dialog).OfType<ComboBox>().Single();
        Assert.Equal(-1, combo.SelectedIndex);
        Assert.False(dialog.OutputSelectionExplicit);
        Assert.False(((Button)dialog.AcceptButton!).Enabled);
        combo.SelectedValue = 1;
        typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(combo, [EventArgs.Empty]);
        Assert.True(dialog.OutputSelectionExplicit);
        Assert.True(((Button)dialog.AcceptButton!).Enabled);
    });

    [Fact]
    public void ProjectRuntimeReset_ZeroEventCanFireOnImmediateStartBeforeNextTimerTick() => WinFormsTest.Run(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        manager.Current.Triggers.Add(new TriggerDefinition
        {
            TriggerType = TriggerType.StopwatchElapsed,
            Parameters = new TriggerParameters { Elapsed = TimeSpan.Zero }
        });
        var triggerEngine = new TriggerEngine();
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output());
        using var form = CreateForm(manager, engine, triggerEngine, () => Initial, directory.Path);
        Initialize(form);
        Method("ResetProjectRuntime").Invoke(form, null);
        Assert.Single(triggerEngine.Tick(DateTimeOffset.Now, TimeSpan.Zero, true));
        Assert.Empty(triggerEngine.Tick(DateTimeOffset.Now, TimeSpan.Zero, true));
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void InitialProbe_EmergencyInvalidatesConfigurationBeforeItCanOpenAnOutput() => WinFormsTest.Run(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        manager.Current.Settings.SilentLoop = true;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var outputs = 0;
        using var engine = new AudioEngine(new Logger(), (_, _) => { outputs++; return new Output(); });
        using var form = CreateForm(manager, engine, new TriggerEngine(), () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) { throw new TimeoutException(); }
            return Initial;
        }, directory.Path);
        form.Show();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Method("EmergencyStopAll").Invoke(form, null);
        }
        finally { release.Set(); }
        Pump((Task)Field("_outputConfigurationTask").GetValue(form)!);
        Assert.Equal(0, outputs);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void OpenPonDashiProject_RendersCuesAndAppliesExclusiveGroupsOnNormalPlay() => WinFormsTest.RunAsync(async () =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var wave = Path.GetFileName(CreateWave(directory));
        var project = directory.GetPath("show");
        File.WriteAllText(project, $"""
            <ProjectSettings><element>
              <SoundElement><name>最初</name><groupNo>1</groupNo><shortCutKey>1</shortCutKey><filePathRel>{wave}</filePathRel></SoundElement>
              <SoundElement><name>次</name><groupNo>1</groupNo><shortCutKey>2</shortCutKey><filePathRel>{wave}</filePathRel></SoundElement>
            </element></ProjectSettings>
            """);
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output());
        using var form = CreateForm(manager, engine, new TriggerEngine(), () => Initial, directory.Path);
        form.Show();
        WinFormsTest.Note("import: configure initial output");
        await (Task)Field("_outputConfigurationTask").GetValue(form)!;
        ((System.Windows.Forms.Timer)Field("_clockTimer").GetValue(form)!).Stop();

        WinFormsTest.Note("import: open extensionless project");
        await (Task)Method("OpenProjectAsync").Invoke(form, [project])!;
        Assert.True(manager.RequiresSaveAs);
        Assert.True(manager.IsDirty);
        Assert.Contains(Descendants(form).OfType<Label>(), label => label.Text == "最初");
        Assert.Contains(Descendants(form).OfType<Label>(), label => label.Text == "次");

        var first = manager.Current.Sounds[0].Id;
        var next = manager.Current.Sounds[1].Id;
        WinFormsTest.Note("import: play first cue");
        await (Task)Method("PlaySoundAsync").Invoke(form, [first])!;
        Assert.Equal(first, Assert.Single(engine.GetActiveSoundIds()));
        WinFormsTest.Note("import: play second cue");
        await (Task)Method("PlaySoundAsync").Invoke(form, [next])!;
        Assert.Equal(next, Assert.Single(engine.GetActiveSoundIds()));
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void ExclusiveSwitch_StopsFadingPeerImmediately() => WinFormsTest.Run(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var path = CreateWave(directory);
        var first = new SoundDefinition { File = path, Group = "BGM", FadeOut = new FadeSettings { Enabled = true, Value = 3 } };
        var second = new SoundDefinition { File = path, Group = "bgm" };
        manager.Current.Sounds.AddRange([first, second]);
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output());
        using var form = CreateForm(manager, engine, new TriggerEngine(), () => Initial, directory.Path);
        Initialize(form);
        engine.PlayAsync(first, path).GetAwaiter().GetResult();
        engine.Stop(first.Id);
        Assert.Equal(pondex.Audio.PlaybackState.Fading, engine.GetState(first.Id));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackStarted += (_, args) => { if (args.SoundId == second.Id) { started.TrySetResult(); } };
        Method("ExecuteShortcuts").Invoke(form, [new[] { new ShortcutCommand(ShortcutCommandType.PlayExclusive, second.Id) }]);
        Assert.Equal(pondex.Audio.PlaybackState.Stopped, engine.GetState(first.Id));
        Pump(started.Task);
        Assert.Equal(second.Id, Assert.Single(engine.GetActiveSoundIds()));
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void DefaultChange_StopsAndRequiresExplicitReselectionEvenAfterReconnection() => WinFormsTest.Run(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var snapshot = Initial;
        var trigger = new TriggerEngine();
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output());
        using var form = CreateForm(manager, engine, trigger, () => Volatile.Read(ref snapshot), directory.Path);
        Initialize(form);
        Assert.Equal(0, engine.OutputDeviceNumber); // WAVE_MAPPER (-1) must never be opened.
        var sound = new SoundDefinition();
        engine.PlayAsync(sound, CreateWave(directory)).GetAwaiter().GetResult();
        Assert.Single(engine.GetActiveSoundIds());
        Volatile.Write(ref snapshot, Initial with { DefaultOutput = new(1, "speakers-endpoint") });
        Pump((Task)Method("CheckSelectedDeviceAvailability").Invoke(form, [true])!);
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.True(trigger.IsSuspended);
        Assert.True((bool)Field("_outputUnavailable").GetValue(form)!);
        Volatile.Write(ref snapshot, Initial);
        Pump((Task)Method("CheckSelectedDeviceAvailability").Invoke(form, [true])!);
        Assert.True((bool)Field("_outputUnavailable").GetValue(form)!);
        Method("ApplyProjectSettings").Invoke(form, null);
        Pump((Task)Field("_outputConfigurationTask").GetValue(form)!);
        Assert.False((bool)Field("_outputUnavailable").GetValue(form)!);
        Assert.True(trigger.IsSuspended);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SlowProbe_DoesNotBlockUiAndPendingPlayCannotReviveAfterStop(bool exclusiveSwitch) => WinFormsTest.Run(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var path = CreateWave(directory);
        var first = new SoundDefinition { File = path, Key = "A", Group = "BGM" };
        var second = new SoundDefinition { File = path, Key = "B", Group = "BGM" };
        manager.Current.Sounds.AddRange([first, second]);
        var block = false;
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var startedSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output());
        engine.PlaybackStarted += (_, args) => { if (args.SoundId == second.Id) { startedSecond.TrySetResult(); } };
        using var form = CreateForm(manager, engine, new TriggerEngine(), () =>
        {
            if (Volatile.Read(ref block))
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) { throw new TimeoutException(); }
            }
            return Initial;
        }, directory.Path);
        Initialize(form);
        Volatile.Write(ref block, true);
        var firstPlay = (Task)Method("PlaySoundAsync").Invoke(form, [first.Id])!;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(firstPlay.IsCompleted);
            // Both actions must finish on the UI thread while the driver probe is blocked.
            if (exclusiveSwitch)
            {
                Method("ExecuteShortcuts").Invoke(form, [new[] { new ShortcutCommand(ShortcutCommandType.PlayExclusive, second.Id) }]);
            }
            else { Method("EmergencyStopAll").Invoke(form, null); }
        }
        finally { release.Set(); }
        Pump(firstPlay);
        if (exclusiveSwitch)
        {
            Pump(startedSecond.Task);
            Assert.Equal(second.Id, Assert.Single(engine.GetActiveSoundIds()));
        }
        else { Assert.Empty(engine.GetActiveSoundIds()); }
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(500)]
    public void CardResize_BatchesParentLayoutsAndDetailsReuseControls(int count) => WinFormsTest.Run(() =>
    {
        WinFormsTest.Note($"card measurement: construct {count} cards");
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        manager.Current.Sounds.AddRange(Enumerable.Range(0, count).Select(i => new SoundDefinition { Name = $"Sound {i}" }));
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output());
        using var form = CreateForm(manager, engine, new TriggerEngine(), () => Initial, directory.Path);
        WinFormsTest.Note($"card measurement: show {count} cards");
        Initialize(form);
        var list = Descendants(form).OfType<ThemedFlowLayoutPanel>().Single();
        var layouts = 0;
        list.Layout += (_, _) => layouts++;
        form.Width += 100;
        Assert.InRange(layouts, 0, 12);
        var cards = list.Controls.OfType<SoundCard>().ToArray();
        var card = cards[0];
        WinFormsTest.Note("card measurement: create details");
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        card.EnsureDetailsCreated();
        var firstDetailsMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var detailsBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var controls = Descendants(card).ToArray();
        var button = Descendants(card).OfType<Button>().Single(b => b.Text.StartsWith("詳細", StringComparison.Ordinal));
        started = Stopwatch.GetTimestamp();
        button.PerformClick();
        var firstExpandMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        started = Stopwatch.GetTimestamp();
        WinFormsTest.Note("card measurement: reuse details");
        for (var i = 0; i < 10; i++) { button.PerformClick(); }
        var reusedToggleMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds / 10;
        Assert.Equal(controls, Descendants(card).ToArray());
        var targetWidth = card.Width;
        var baseline = new List<double>();
        var batched = new List<double>();
        var resizeBytes = new List<long>();
        // Keep the intentionally quadratic legacy comparison bounded at 500 cards.
        // All functional assertions still run for every size; report sample count.
        var measurementOrder = count >= 500 ? LargeResizeMeasurementOrder : ResizeMeasurementOrder;
        foreach (var before in measurementOrder)
        {
            WinFormsTest.Note($"card measurement: resize {count}, legacy={before}");
            list.SuspendLayout();
            foreach (var item in cards) { item.Width = targetWidth + 10; }
            list.ResumeLayout(true);
            started = Stopwatch.GetTimestamp();
            allocated = GC.GetAllocatedBytesForCurrentThread();
            if (before) { foreach (var item in cards) { item.Width = targetWidth; } }
            else { Method("ResizeSoundCards").Invoke(form, null); }
            (before ? baseline : batched).Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (!before) { resizeBytes.Add(GC.GetAllocatedBytesForCurrentThread() - allocated); }
        }
        var header = (TableLayoutPanel)Descendants(form).OfType<TrackBar>().First().Parent!.Parent!;
        var visibleRows = cards.Where(item => list.ClientRectangle.Contains(item.Bounds)).Select(item => item.Top).Distinct().Count();
        var initialScroll = list.AutoScrollPosition;
        allocated = GC.GetAllocatedBytesForCurrentThread();
        started = Stopwatch.GetTimestamp();
        // Measure a fixed sequence of viewport movements on the actual list.
        for (var frame = 1; frame <= 30; frame++) { list.AutoScrollPosition = new Point(0, frame * 12); }
        var scrollMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var scrollBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(-list.AutoScrollPosition.Y > -initialScroll.Y);
        list.AutoScrollPosition = new Point(-initialScroll.X, -initialScroll.Y);

        started = Stopwatch.GetTimestamp();
        Method("OnProjectChanged").Invoke(form, [null, EventArgs.Empty]);
        var savedUiMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        output.WriteLine("NativeUiPerf " + JsonSerializer.Serialize(new
        {
            count,
            resize_samples = baseline.Count,
            first_details_ms = firstDetailsMs,
            first_expand_ms = firstExpandMs,
            saved_ui_ms = savedUiMs,
            reused_toggle_ms = reusedToggleMs,
            resize_before_ms = baseline.Order().ElementAt(baseline.Count / 2),
            resize_after_ms = batched.Order().ElementAt(batched.Count / 2),
            layout_count = layouts,
            details_allocated_bytes = detailsBytes,
            resize_allocated_bytes = resizeBytes.Order().ElementAt(resizeBytes.Count / 2),
            scroll_ms_30_steps = scrollMs,
            scroll_allocated_bytes = scrollBytes,
            header_height = header.Height,
            fully_visible_rows = visibleRows
        }));
        form.CloseWithoutSavePromptForSmokeTest();
    }, timeout: TimeSpan.FromMinutes(2));

    private static MainForm CreateForm(ProjectManager manager, AudioEngine engine, TriggerEngine triggers,
        Func<AudioDeviceSnapshot> probe, string directory) =>
        new(manager, engine, new AudioDeviceManager(probe), triggers, new Logger(), directory)
        { ShowInTaskbar = false, Opacity = 0, Font = UiTheme.CreateFont(10F) };

    private static void Initialize(MainForm form)
    {
        form.Show();
        Pump((Task)Field("_outputConfigurationTask").GetValue(form)!);
        ((System.Windows.Forms.Timer)Field("_clockTimer").GetValue(form)!).Stop();
    }

    private static void Pump(Task task)
    {
        var deadline = Environment.TickCount64 + 12000;
        while (!task.IsCompleted && Environment.TickCount64 < deadline) { Application.DoEvents(); Thread.Yield(); }
        Assert.True(task.IsCompleted, "UI operation timed out");
        task.GetAwaiter().GetResult();
    }

    private static MethodInfo Method(string name) => typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static FieldInfo Field(string name) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static string CreateWave(TestDirectory directory)
    {
        var path = directory.GetPath("test.wav");
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
        for (var i = 0; i < 100; i++) { writer.WriteSample(0.05F); }
        return path;
    }
    private sealed class Logger : IAppLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
    private sealed class BlockingStopOutput(ManualResetEventSlim entered, ManualResetEventSlim release) : IAudioOutput
    {
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add { } remove { } }
        public NAudio.Wave.PlaybackState PlaybackState { get; private set; }
        public void Play() => PlaybackState = NAudio.Wave.PlaybackState.Playing;
        public void Stop()
        {
            if (PlaybackState != NAudio.Wave.PlaybackState.Playing) { return; }
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) { throw new TimeoutException(); }
            PlaybackState = NAudio.Wave.PlaybackState.Stopped;
        }
        public void Dispose() { }
    }

    private sealed class Output : IAudioOutput
    {
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add { } remove { } }
        public NAudio.Wave.PlaybackState PlaybackState { get; private set; }
        public void Play() => PlaybackState = NAudio.Wave.PlaybackState.Playing;
        public void Stop() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;
        public void Dispose() => Stop();
    }
}
