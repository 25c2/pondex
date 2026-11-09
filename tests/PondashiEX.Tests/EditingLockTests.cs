using System.Reflection;
using System.Text.Json;
using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Input;
using PondashiEX.Project;
using PondashiEX.Trigger;
using PondashiEX.UI;
using PondashiEX.Utility;

namespace PondashiEX.Tests;

[Collection(WinFormsTestGroup.Name)]
public sealed class EditingLockTests
{
    [Theory]
    [InlineData(1F, 1F)]
    [InlineData(1F, 1.75F)]
    public void Lock_BlocksSettingsVolumesProjectReplacementAndQueuedDrops(float masterGain, float soundGain) => WinFormsTest.Run(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var sound = new SoundDefinition { Name = "cue", Group = "SE", Volume = soundGain, AllowVolumeBoost = soundGain > 1 };
        manager.Current.Settings.MasterVolume = masterGain;
        manager.Current.Sounds.Add(sound);
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        var card = Descendants(form).OfType<SoundCard>().Single();
        card.EnsureDetailsCreated();
        Call(card, "ShowPlaybackSettings");
        var dialog = form.OwnedForms.OfType<PlaybackSettingsDialog>().Single();
        var before = JsonSerializer.Serialize(manager.Current);
        var originalProject = manager.Current;
        form.BeginInvoke(() => Call(form, "AddSoundFiles", (object)new[] { directory.GetPath("queued.wav") }));

        Call(form, "SetEditingLocked", true);
        Application.DoEvents();

        Assert.True(dialog.IsDisposed);
        Assert.Contains("[編集ロック]", form.Text, StringComparison.Ordinal);
        var master = Field<NumericUpDown>(form, "_masterVolumeValue");
        Assert.False(master.Enabled);
        Assert.False(Field<TrackBar>(form, "_masterVolume").Enabled);
        Assert.False(Field<CheckBox>(form, "_masterMute").Enabled);
        Assert.False(Field<Button>(form, "_addSoundButton").Enabled);
        Assert.False(Field<FlowLayoutPanel>(card, "_detailsContent").Enabled);
        Assert.False(Field<TrackBar>(card, "_volumeTrack").Enabled);
        master.ContextMenuStrip!.Items[0].PerformClick();
        Field<NumericUpDown>(card, "_volumeValue").ContextMenuStrip!.Items[0].PerformClick();
        master.Value = 37;
        Field<CheckBox>(form, "_masterMute").Checked = true;
        Field<NumericUpDown>(card, "_volumeValue").Value = 42;
        Field<TrackBar>(card, "_volumeTrack").Value = 35;
        Field<TextBox>(card, "_nameText").Text = "blocked";
        Call(form, "ToggleMasterMute");
        Call(form, "AddSoundFiles", (object)new[] { directory.GetPath("blocked.wav") });
        Call(form, "DeleteSound", sound, card);
        Call(form, "BrowseReplacementFile", sound, card);
        Call(form, "ShowSettings");
        Call(form, "ShowSoundOrder");
        Call(form, "ShowGroupShortcuts");
        Complete((Task)Call(form, "NewProjectAsync")!);
        Complete((Task)Call(form, "OpenProjectAsync", directory.GetPath("missing.pondex"))!);
        Complete((Task)Call(form, "RunSelfTestAsync")!);
        Assert.Same(originalProject, manager.Current);
        Assert.Equal(before, JsonSerializer.Serialize(manager.Current));
        Assert.False(manager.IsDirty);
        Assert.Equal((decimal)(int)(masterGain * 100), master.Value);
        Assert.Equal((decimal)(int)(soundGain * 100), Field<NumericUpDown>(card, "_volumeValue").Value);
        Assert.Equal(masterGain, engine.MasterVolume);
        Assert.False(engine.MasterMute);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void Lock_KeepsTransportShortcutsAndEmergencyStopUsable() => WinFormsTest.Run(() =>
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("cue.wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 1)))
        {
            writer.Write(new byte[8820], 0, 8820);
        }
        using var manager = new ProjectManager(new ProjectSerializer());
        var sound = new SoundDefinition { File = path, Key = "A" };
        manager.Current.Sounds.Add(sound);
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        Call(form, "SetEditingLocked", true);
        Complete((Task)Call(form, "PlaySoundAsync", sound.Id)!);
        Assert.Equal(PondashiEX.Audio.PlaybackState.Playing, engine.GetState(sound.Id));
        Application.DoEvents();
        var card = Descendants(form).OfType<SoundCard>().Single();
        var pause = Field<Button>(card, "_pauseButton");
        Assert.True(pause.Enabled);
        pause.PerformClick();
        Assert.Equal(PondashiEX.Audio.PlaybackState.Paused, engine.GetState(sound.Id));
        pause.PerformClick();
        Assert.Equal(PondashiEX.Audio.PlaybackState.Playing, engine.GetState(sound.Id));
        Call(form, "ExecuteShortcuts", (object)new[] { new ShortcutCommand(ShortcutCommandType.Stop, sound.Id) });
        Assert.Empty(engine.GetActiveSoundIds());
        var dispatcher = Field<ShortcutDispatcher>(form, "_shortcutDispatcher");
        form.Activate();
        card.EndEditing();
        Application.DoEvents();
        dispatcher.OnKeyChanged(Keys.A, true);
        Assert.True(SpinWait.SpinUntil(() => engine.GetState(sound.Id) == PondashiEX.Audio.PlaybackState.Playing, TimeSpan.FromSeconds(5)));
        dispatcher.OnKeyChanged(Keys.A, false);
        Call(form, "EmergencyStopAll");
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.True(Field<bool>(form, "_editingLocked"));
        Assert.False(Field<NumericUpDown>(form, "_masterVolumeValue").Enabled);
        Assert.True(Field<TriggerEngine>(form, "_triggerEngine").IsSuspended);
        Field<ToolStripButton>(form, "_resumeTriggersButton").PerformClick();
        Assert.False(Field<TriggerEngine>(form, "_triggerEngine").IsSuspended);
        Assert.True(Field<bool>(form, "_editingLocked"));
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void Lock_SurvivesSaveAndCardRebuildAndRequiresExplicitUnlockMenuAction() => WinFormsTest.Run(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        manager.Current.Sounds.Add(new SoundDefinition { File = directory.GetPath("cue.wav") });
        manager.SaveAsAsync(directory.GetPath("show.pondex")).GetAwaiter().GetResult();
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        Call(form, "SetEditingLocked", true);
        var save = (Task<bool>)Call(form, "SaveProjectAsync", false)!;
        Complete(save);
        Assert.True(save.Result);
        Assert.True(Field<bool>(form, "_editingLocked"));
        Call(form, "RebuildSoundCards");
        var card = Descendants(form).OfType<SoundCard>().Single();
        Assert.False(card.DetailsCreated);
        Assert.False(Field<TrackBar>(card, "_volumeTrack").Enabled);
        card.EnsureDetailsCreated();
        Assert.False(Field<FlowLayoutPanel>(card, "_detailsContent").Enabled);
        var lockButton = Field<ToolStripDropDownButton>(form, "_editLockButton");
        lockButton.PerformClick();
        Assert.True(Field<bool>(form, "_editingLocked"));
        lockButton.HideDropDown();
        lockButton.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "編集ロックを解除").PerformClick();
        Assert.False(Field<bool>(form, "_editingLocked"));
        Assert.True(Field<NumericUpDown>(form, "_masterVolumeValue").Enabled);
        Assert.True(Field<FlowLayoutPanel>(card, "_detailsContent").Enabled);
        Assert.True(Field<Button>(form, "_addSoundButton").Enabled);
        Field<NumericUpDown>(form, "_masterVolumeValue").Value = 50;
        Field<NumericUpDown>(card, "_volumeValue").Value = 40;
        Assert.Equal(0.5F, manager.Current.Settings.MasterVolume);
        Assert.Equal(0.4F, card.Sound.Volume);
        Assert.True(manager.IsDirty);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void TriggerLock_BlocksCheckboxAndMutationCommandsWhileKeepingListReadable() => WinFormsTest.Run(() =>
    {
        using var form = new Form();
        using var panel = new TriggerPanel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        var sound = new SoundDefinition();
        var definition = new TriggerDefinition { TargetSoundId = sound.Id };
        var triggers = new List<TriggerDefinition> { definition };
        panel.SetData(triggers, [sound]);
        form.Show();
        panel.SetEditingLocked(true);
        var grid = Descendants(panel).OfType<DataGridView>().Single();
        Assert.True(grid.Enabled);
        Assert.True(grid.ReadOnly);
        foreach (var button in Descendants(panel).OfType<Button>()) { Assert.False(button.Enabled); }
        Call(panel, "AddTrigger");
        Call(panel, "EditSelectedTrigger");
        Call(panel, "DeleteSelectedTrigger");
        Call(panel, "ToggleSelectedTrigger");
        grid.Rows[0].Cells[0].Value = false;
        Assert.True(definition.Enabled);
        Assert.Single(triggers);
        panel.SetData(triggers, [sound]);
        Assert.True(grid.ReadOnly);
        panel.SetEditingLocked(false);
        Assert.False(grid.ReadOnly);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        Call(panel, "ToggleSelectedTrigger");
        Assert.False(definition.Enabled);
    });

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static object? Call(object target, string name, params object[] arguments) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);

    private static void Complete(Task task)
    {
        var deadline = Environment.TickCount64 + 15000;
        while (!task.IsCompleted && Environment.TickCount64 < deadline) { Application.DoEvents(); Thread.Yield(); }
        Assert.True(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) { yield return descendant; }
        }
    }

    private static MainForm CreateForm(ProjectManager manager, AudioEngine engine, string directory) =>
        new(manager, engine, new AudioDeviceManager(), new TriggerEngine(), new NullLogger(), directory)
        { ShowInTaskbar = false, Opacity = 0, Font = UiTheme.CreateFont(10F) };

    private static AudioEngine CreateEngine() => new(new NullLogger(), (_, _) => new FakeOutput());

    private sealed class FakeOutput : IAudioOutput
    {
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add { } remove { } }
        public NAudio.Wave.PlaybackState PlaybackState { get; private set; }
        public void Play() => PlaybackState = NAudio.Wave.PlaybackState.Playing;
        public void Stop() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;
        public void Dispose() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
