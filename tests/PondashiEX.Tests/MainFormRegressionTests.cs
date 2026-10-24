using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Input;
using PondashiEX.Project;
using PondashiEX.Trigger;
using PondashiEX.UI;
using PondashiEX.Utility;

namespace PondashiEX.Tests;

public sealed class MainFormRegressionTests
{
    [Fact]
    public void SaveDuringEdits_DoesNotAuthorizeClosingOrReplacingProject() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var path = directory.GetPath("show.pondex");
        manager.SaveAsAsync(path).GetAwaiter().GetResult();
        // A large snapshot ensures real asynchronous file I/O without a real audio device.
        manager.Current.Sounds = Enumerable.Range(0, 400).Select(index => new SoundDefinition
        {
            Name = $"音源 {index}",
            Memo = new string('x', 4096)
        }).ToList();
        manager.MarkDirty();
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        var saveTask = (Task<bool>)Method("SaveProjectAsync").Invoke(form, [false])!;
        Assert.False(saveTask.IsCompleted);
        manager.Current.Settings.MasterVolume = 0.37F;
        manager.MarkDirty();
        PumpUntil(saveTask);

        Assert.False(saveTask.GetAwaiter().GetResult());
        Assert.True(manager.IsDirty);
        Assert.Equal(0.37F, manager.Current.Settings.MasterVolume);
        Assert.Equal(1F, new ProjectSerializer().LoadAsync(path).GetAwaiter().GetResult().Settings.MasterVolume);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void DialogTextInput_IsBlockedFromNormalPlaybackShortcuts() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        using var dialog = new SettingsDialog(manager.Current.Settings, [AudioDeviceInfo.Default]);
        dialog.Show(form);
        dialog.Activate();
        var editor = Descendants(dialog).OfType<TextBox>().First();
        editor.Focus();
        Application.DoEvents();
        Assert.True((bool)Method("IsEditingText").Invoke(form, null)!);
        dialog.Close();
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void BusyProjectOperation_BlocksClosingAndDroppedFiles() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        Method("SetProjectOperationBusy").Invoke(form, [true]);
        var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
        Method("OnFormClosing").Invoke(form, [form, closing]);
        Assert.True(closing.Cancel);
        var data = new DataObject(DataFormats.FileDrop, new[] { directory.GetPath("dropped.wav") });
        var drop = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
        Method("OnDragDrop").Invoke(form, [form, drop]);
        Assert.Empty(manager.Current.Sounds);
        Method("SetProjectOperationBusy").Invoke(form, [false]);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void MinimumWindow_KeepsMasterVolumeUsable() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Size = form.MinimumSize;
        form.Show();
        Application.DoEvents();
        var master = Assert.Single(Descendants(form).OfType<TrackBar>());
        Assert.True(master.Width >= 80, $"MASTERスライダー幅: {master.Width}");
        var header = (TableLayoutPanel)master.Parent!.Parent!;
        Assert.Equal(3, header.ColumnCount);
        form.ClientSize = new Size(1500, 760);
        Application.DoEvents();
        Assert.Equal(6, header.ColumnCount);
        Assert.True(master.Width >= 80);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void Deactivation_ResetsHeldShortcutKeys() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        var dispatcher = (ShortcutDispatcher)typeof(MainForm).GetField("_shortcutDispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        dispatcher.OnKeyChanged(Keys.A, true);
        typeof(Form).GetMethod("OnDeactivate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [EventArgs.Empty]);
        var pressed = (HashSet<Keys>)typeof(ShortcutDispatcher).GetField("_pressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dispatcher)!;
        Assert.Empty(pressed);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ReorderDialog_UpdatesActualCardsWithoutRecreatingThem(int columns) => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var sounds = Enumerable.Range(1, 8).Select(index => new SoundDefinition { Name = $"音源 {index}" }).ToList();
        manager.Current.Sounds.AddRange(sounds);
        manager.Current.Settings.SoundListColumns = columns;
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        Application.DoEvents();
        var cards = Descendants(form).OfType<SoundCard>().ToArray();
        var firstCard = cards.Single(card => card.Sound.Id == sounds[0].Id);
        Descendants(firstCard).OfType<Button>().Single(button => button.Text.StartsWith("詳細", StringComparison.Ordinal)).PerformClick();
        var expandedHeight = firstCard.Height;
        var wavePath = directory.GetPath("playing.wav");
        using (var writer = new WaveFileWriter(wavePath, new WaveFormat(44_100, 16, 1)))
        {
            writer.Write(new byte[8_820], 0, 8_820);
        }

        engine.PlayAsync(sounds[0], wavePath).GetAwaiter().GetResult();
        Application.DoEvents();
        var menu = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>()
            .SelectMany(item => item.DropDownItems.OfType<ToolStripMenuItem>())
            .Single(item => item.Text?.StartsWith("音源の並び替え", StringComparison.Ordinal) == true);
        Exception? callbackFailure = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            var dialog = form.OwnedForms.OfType<SoundOrderDialog>().SingleOrDefault();
            if (dialog is null)
            {
                return;
            }

            timer.Stop();
            try
            {
                Assert.True(dialog.MoveSelectedTo(6));
                Descendants(dialog).OfType<Button>().Single(button => button.Text == "適用").PerformClick();
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
                dialog.DialogResult = DialogResult.Cancel;
                dialog.Close();
            }
        };
        timer.Start();

        menu.PerformClick();
        Application.DoEvents();

        Assert.Null(callbackFailure);
        var expected = sounds.Skip(1).Take(6).Append(sounds[0]).Append(sounds[7]).ToArray();
        Assert.Equal(expected, manager.Current.Sounds);
        var list = firstCard.Parent!;
        Assert.Equal(expected, list.Controls.OfType<SoundCard>().Select(card => card.Sound));
        Assert.Same(firstCard, list.Controls[6]);
        Assert.Equal(expandedHeight, firstCard.Height);
        Assert.Equal(PondashiEX.Audio.PlaybackState.Playing, engine.GetState(sounds[0].Id));
        Assert.True(manager.IsDirty);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    private static MainForm CreateForm(ProjectManager manager, AudioEngine engine, string logDirectory) =>
        new(manager, engine, new AudioDeviceManager(), new TriggerEngine(), new NullLogger(), logDirectory)
        { ShowInTaskbar = false, Opacity = 0 };

    private static AudioEngine CreateEngine() => new(new NullLogger(), (_, _) => new FakeOutput());

    private static MethodInfo Method(string name) => typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;

    private static void PumpUntil(Task task)
    {
        var deadline = Environment.TickCount64 + 15_000;
        while (!task.IsCompleted && Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Yield();
        }

        Assert.True(task.IsCompleted, "UI操作が完了しませんでした。");
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UIテストがタイムアウトしました。");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) { yield return descendant; }
        }
    }

    private sealed class FakeOutput : IAudioOutput
    {
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add { } remove { } }
        public NAudio.Wave.PlaybackState PlaybackState => NAudio.Wave.PlaybackState.Stopped;
        public void Play() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
