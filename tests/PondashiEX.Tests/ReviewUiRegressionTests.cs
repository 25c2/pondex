using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;
using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Input;
using PondashiEX.Project;
using PondashiEX.Trigger;
using PondashiEX.UI;
using PondashiEX.Utility;

namespace PondashiEX.Tests;

[Collection(WinFormsTestGroup.Name)]
public sealed class ReviewUiRegressionTests(ITestOutputHelper output)
{
    private static readonly bool[] ResizeMeasurementOrder = [true, false, false, true, true, false];
    private static readonly AudioDeviceSnapshot Initial = new(
        [AudioDeviceInfo.Default, new AudioDeviceInfo(0, "Console", "console"), new AudioDeviceInfo(1, "Speakers", "speakers")], new(0, "console-endpoint"));

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
        Assert.Equal(PondashiEX.Audio.PlaybackState.Fading, engine.GetState(first.Id));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackStarted += (_, args) => { if (args.SoundId == second.Id) { started.TrySetResult(); } };
        Method("ExecuteShortcuts").Invoke(form, [new[] { new ShortcutCommand(ShortcutCommandType.PlayExclusive, second.Id) }]);
        Assert.Equal(PondashiEX.Audio.PlaybackState.Stopped, engine.GetState(first.Id));
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
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        manager.Current.Sounds.AddRange(Enumerable.Range(0, count).Select(i => new SoundDefinition { Name = $"Sound {i}" }));
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output());
        using var form = CreateForm(manager, engine, new TriggerEngine(), () => Initial, directory.Path);
        Initialize(form);
        var list = Descendants(form).OfType<ThemedFlowLayoutPanel>().Single();
        var layouts = 0;
        list.Layout += (_, _) => layouts++;
        form.Width += 100;
        Assert.InRange(layouts, 0, 12);
        var cards = list.Controls.OfType<SoundCard>().ToArray();
        var card = cards[0];
        var started = Stopwatch.GetTimestamp();
        card.EnsureDetailsCreated();
        var firstDetailsMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var controls = Descendants(card).ToArray();
        var button = Descendants(card).OfType<Button>().Single(b => b.Text.StartsWith("詳細", StringComparison.Ordinal));
        started = Stopwatch.GetTimestamp();
        button.PerformClick();
        var firstExpandMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        started = Stopwatch.GetTimestamp();
        for (var i = 0; i < 10; i++) { button.PerformClick(); }
        var reusedToggleMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds / 10;
        Assert.Equal(controls, Descendants(card).ToArray());
        var targetWidth = card.Width;
        var baseline = new List<double>();
        var batched = new List<double>();
        foreach (var before in ResizeMeasurementOrder)
        {
            list.SuspendLayout();
            foreach (var item in cards) { item.Width = targetWidth + 10; }
            list.ResumeLayout(true);
            started = Stopwatch.GetTimestamp();
            if (before) { foreach (var item in cards) { item.Width = targetWidth; } }
            else { Method("ResizeSoundCards").Invoke(form, null); }
            (before ? baseline : batched).Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        started = Stopwatch.GetTimestamp();
        Method("OnProjectChanged").Invoke(form, [null, EventArgs.Empty]);
        var savedUiMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        output.WriteLine("NativeUiPerf " + JsonSerializer.Serialize(new
        {
            count,
            first_details_ms = firstDetailsMs,
            first_expand_ms = firstExpandMs,
            saved_ui_ms = savedUiMs,
            reused_toggle_ms = reusedToggleMs,
            resize_before_ms = baseline.Order().ElementAt(1),
            resize_after_ms = batched.Order().ElementAt(1),
            layouts
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
    private sealed class Output : IAudioOutput
    {
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add { } remove { } }
        public NAudio.Wave.PlaybackState PlaybackState { get; private set; }
        public void Play() => PlaybackState = NAudio.Wave.PlaybackState.Playing;
        public void Stop() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;
        public void Dispose() => Stop();
    }
}
