using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Input;
using PondashiEX.Project;
using PondashiEX.Trigger;
using PondashiEX.UI;
using PondashiEX.Utility;

namespace PondashiEX.Tests;

[Collection("Application color mode")]
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
        form.MaximumSize = new Size(1024, 768);
        form.Size = form.MinimumSize;
        form.Show();
        Application.DoEvents();
        var master = Assert.Single(Descendants(form).OfType<TrackBar>());
        Assert.True(master.Width >= 80, $"MASTERスライダー幅: {master.Width}");
        var header = (TableLayoutPanel)master.Parent!.Parent!;
        Assert.Equal(3, header.ColumnCount);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void HeaderLayout_SwitchesBothWaysWithoutDependingOnDesktopResolution() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        // A small CI desktop must not prevent testing the wide header layout.
        form.MaximumSize = new Size(1024, 768);
        form.Show();
        Application.DoEvents();
        var master = Assert.Single(Descendants(form).OfType<TrackBar>());
        var header = (TableLayoutPanel)master.Parent!.Parent!;
        // Size the actual header directly: Windows can clamp top-level form sizes.
        header.Dock = DockStyle.None;
        ReadOnlySpan<int> logicalWidths = [900, 1100, 1500, 1099, 900, 1500];
        foreach (var logicalWidth in logicalWidths)
        {
            var compact = logicalWidth < 1100;
            var width = (int)(logicalWidth * header.DeviceDpi / 96F);
            header.Size = new Size(width, (int)((compact ? 210 : 104) * header.DeviceDpi / 96F));
            Application.DoEvents();
            Assert.Equal(width, header.ClientSize.Width);
            Assert.Equal(compact ? 3 : 6, header.ColumnCount);
            Assert.Equal(compact ? 5 : 2, header.RowCount);
            Assert.True(master.Width >= 80, $"幅{logicalWidth}のMASTERスライダー幅: {master.Width}");
        }

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
    [InlineData(8)]
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

    [Fact]
    public void DarkMode_ThemesNativeWindowsAndCanBeSwitchedAtRuntime() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        manager.Current.Settings.DarkMode = true;
        manager.Current.Sounds.AddRange(Enumerable.Range(1, 8)
            .Select(index => new SoundDefinition { Name = $"ダークモード確認 {index}" }));
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        Application.DoEvents();
        Assert.Equal(SystemColorMode.Dark, Application.ColorMode);
        AssertDarkCaption(form, true);
        var soundList = Descendants(form).OfType<ThemedFlowLayoutPanel>().Single();
        soundList.AutoScrollPosition = new Point(0, 75);
        var oldScrollHandle = soundList.Handle;
        var scrollPosition = soundList.AutoScrollPosition;
        using var dialog = new PlaybackSettingsDialog(manager.Current.Sounds[0])
        { ShowInTaskbar = false, Opacity = 0, Font = UiTheme.CreateFont(10F) };
        UiTheme.Apply(dialog, true);
        dialog.Show(form);
        Application.DoEvents();
        AssertDarkCaption(dialog, true);
        CaptureSnapshot(form, "dark-main.png");
        CaptureSnapshot(dialog, "dark-playback.png");
        dialog.Close();

        manager.Current.Settings.DarkMode = false;
        Method("ApplyProjectSettings").Invoke(form, null);
        Application.DoEvents();
        Assert.Equal(SystemColorMode.Classic, Application.ColorMode);
        AssertDarkCaption(form, false);
        Assert.NotEqual(oldScrollHandle, soundList.Handle);
        Assert.Equal(scrollPosition, soundList.AutoScrollPosition);
        CaptureSnapshot(form, "light-main.png");
        form.CloseWithoutSavePromptForSmokeTest();
    });

    private static void AssertDarkCaption(Form form, bool expected)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !SystemInformation.HighContrast)
        {
            Assert.Equal(0, DwmGetWindowAttribute(form.Handle, 20, out var darkMode, sizeof(int)));
            Assert.Equal(expected ? 1 : 0, darkMode);
        }
    }

    [Theory]
    [InlineData(false, Appearance.Normal, false)]
    [InlineData(true, Appearance.Normal, false)]
    [InlineData(false, Appearance.Button, false)]
    [InlineData(true, Appearance.Button, false)]
    [InlineData(false, Appearance.Normal, true)]
    [InlineData(true, Appearance.Normal, true)]
    [InlineData(false, Appearance.Button, true)]
    [InlineData(true, Appearance.Button, true)]
    public void CheckBoxesToggleFromNativeClickAndSpace(bool darkMode, Appearance appearance, bool hidden) => RunOnSta(() =>
    {
        UiTheme.ConfigureApplicationColors(darkMode);
        try
        {
            using var form = new Form
            {
                ClientSize = new Size(420, 140),
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterScreen
            };
            using var check = new CheckBox { Text = "入力確認", Location = new Point(20, 30), Size = new Size(300, 40), Appearance = appearance };
            form.Controls.Add(check);
            UiTheme.Apply(form, darkMode);
            form.Show();
            Application.DoEvents();
            if (hidden)
            {
                // Keep real handles, but remove the interactive desktop hit-test prerequisite.
                form.Hide();
            }

            Assert.True(check.IsHandleCreated);
            var changes = 0;
            var clicks = 0;
            check.CheckedChanged += (_, _) => changes++;
            check.Click += (_, _) => clicks++;
            ClickCheckBox(check);
            Assert.True(check.Checked, $"クリックでONにできません: {appearance}, dark={darkMode}, style={check.FlatStyle}, hidden={hidden}");
            ClickCheckBox(check);
            Assert.False(check.Checked, "クリックでOFFに戻せません。");
            check.Focus();
            SendMessage(check.Handle, 0x0100 /* WM_KEYDOWN */, (IntPtr)Keys.Space, IntPtr.Zero);
            SendMessage(check.Handle, 0x0101 /* WM_KEYUP */, (IntPtr)Keys.Space, IntPtr.Zero);
            Assert.True(check.Checked, "SpaceでONにできません。");
            Assert.Equal(3, changes);
            Assert.Equal(3, clicks);
            form.Close();
        }
        finally
        {
            UiTheme.ConfigureApplicationColors(false);
        }
    });

    private static void ClickCheckBox(CheckBox checkBox)
    {
        // BM_CLICK uses WinForms' real OnClick/AutoCheck path without WindowFromPoint or mouse capture.
        // Raw WM_LBUTTONDOWN/UP requires an interactive desktop, which hosted CI does not guarantee.
        SendMessage(checkBox.Handle, 0x00F5 /* BM_CLICK */, IntPtr.Zero, IntPtr.Zero);
        Application.DoEvents();
    }

    private static void ClickControl(Control control, Point? location = null)
    {
        var point = location ?? new Point(Math.Min(10, control.Width / 2), control.Height / 2);
        var position = (IntPtr)((point.Y << 16) | (point.X & 0xFFFF));
        SendMessage(control.Handle, 0x0201 /* WM_LBUTTONDOWN */, (IntPtr)1, position);
        SendMessage(control.Handle, 0x0202 /* WM_LBUTTONUP */, IntPtr.Zero, position);
        Application.DoEvents();
    }

    [Fact]
    public void DarkCheckBox_CheckedGlyphIsClearlyDifferentFromUnchecked() => RunOnSta(() =>
    {
        UiTheme.ConfigureApplicationColors(true);
        try
        {
            using var form = new Form { ClientSize = new Size(240, 80), ShowInTaskbar = false, Opacity = 0 };
            using var check = new CheckBox { Text = "状態確認", Size = new Size(200, 36), Location = new Point(10, 10) };
            form.Controls.Add(check);
            UiTheme.Apply(form, true);
            form.Show();
            using var off = new Bitmap(check.Width, check.Height);
            using var on = new Bitmap(check.Width, check.Height);
            check.DrawToBitmap(off, check.ClientRectangle);
            check.Checked = true;
            check.DrawToBitmap(on, check.ClientRectangle);
            var distinctPixels = 0;
            for (var x = 0; x < 20; x++)
            {
                for (var y = 0; y < check.Height; y++)
                {
                    var before = off.GetPixel(x, y);
                    var after = on.GetPixel(x, y);
                    if (Math.Max(Math.Abs(before.R - after.R), Math.Max(Math.Abs(before.G - after.G), Math.Abs(before.B - after.B))) >= 80)
                    {
                        distinctPixels++;
                    }
                }
            }

            Assert.True(distinctPixels >= 30, $"ON/OFFを判別できる画素が少なすぎます: {distinctPixels}");
            form.Close();
        }
        finally
        {
            UiTheme.ConfigureApplicationColors(false);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SoundCardCheckBoxes_NativeClicksUpdateDefinitionsAndMute(bool darkMode) => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        manager.Current.Settings.DarkMode = darkMode;
        var sound = new SoundDefinition();
        manager.Current.Sounds.Add(sound);
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Opacity = 1;
        form.TopMost = true;
        form.Show();
        Application.DoEvents();
        var card = Descendants(form).OfType<SoundCard>().Single();
        Descendants(card).OfType<Button>().Single(button => button.Text.StartsWith("詳細", StringComparison.Ordinal)).PerformClick();
        Application.DoEvents();
        var checks = Descendants(card).OfType<CheckBox>().ToArray();
        foreach (var check in checks)
        {
            var changes = 0;
            check.CheckedChanged += (_, _) => changes++;
            CaptureCheckSnapshot(check, $"{darkMode}-{check.Text}-off.png");
            ClickCheckBox(check);
            CaptureCheckSnapshot(check, $"{darkMode}-{check.Text}-on.png");
            Assert.True(check.Checked, $"クリックでONにできません: {check.Text}, dark={darkMode}");
            var enabled = check.Text switch
            {
                "ループ" => sound.Loop.Enabled,
                "Fade In" => sound.FadeIn.Enabled,
                "Fade Out" => sound.FadeOut.Enabled,
                _ => throw new InvalidOperationException(check.Text)
            };
            Assert.True(enabled, $"モデルに反映されません: {check.Text}");
            Assert.Equal(1, changes);
            ClickCheckBox(check);
            Assert.False(check.Checked);
        }

        var mute = Descendants(form).OfType<CheckBox>().Single(check => check.Appearance == Appearance.Button);
        ClickCheckBox(mute);
        Assert.True(mute.Checked);
        Assert.True(engine.MasterMute);
        Assert.True(manager.Current.Settings.MasterMute);
        ClickCheckBox(mute);
        Assert.False(engine.MasterMute);
        form.CloseWithoutSavePromptForSmokeTest();
        UiTheme.ConfigureApplicationColors(false);
    });

    private static void CaptureCheckSnapshot(Control control, string name)
    {
        var snapshotDirectory = Environment.GetEnvironmentVariable("PONDASHI_UI_SNAPSHOT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(snapshotDirectory)) { return; }
        Directory.CreateDirectory(snapshotDirectory);
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, control.ClientRectangle);
        bitmap.Save(Path.Combine(snapshotDirectory, name), System.Drawing.Imaging.ImageFormat.Png);
    }

    [Fact]
    public void SoundListWheelScroll_AnimatesAndNativeScrollbarOverridesIt() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        manager.Current.Sounds.AddRange(Enumerable.Range(1, 30).Select(index => new SoundDefinition { Name = $"音源 {index}" }));
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        Application.DoEvents();
        var list = Descendants(form).OfType<ThemedFlowLayoutPanel>().Single();
        Assert.True(list.VerticalScroll.Visible);
        SendWheel(list, -120);
        Assert.Equal(0, -list.AutoScrollPosition.Y);
        Assert.True(list.IsSmoothScrolling);
        list.AdvanceScrolling(TimeSpan.FromMilliseconds(16));
        var firstFrame = -list.AutoScrollPosition.Y;
        Assert.True(firstFrame > 0);
        for (var frame = 0; frame < 40; frame++) { list.AdvanceScrolling(TimeSpan.FromMilliseconds(16)); }
        Assert.True(-list.AutoScrollPosition.Y > firstFrame);
        Assert.False(list.IsSmoothScrolling);
        var expectedPosition = -list.AutoScrollPosition.Y;

        SendMessage(list.Handle, 0x0115 /* WM_VSCROLL */, (IntPtr)6 /* SB_TOP */, IntPtr.Zero);
        SendWheel(list, -120);
        var deadline = Environment.TickCount64 + 3000;
        while (list.IsSmoothScrolling && Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Yield();
        }

        Assert.False(list.IsSmoothScrolling);
        Assert.Equal(expectedPosition, -list.AutoScrollPosition.Y);

        SendWheel(list, -120);
        Assert.True(list.IsSmoothScrolling);
        SendMessage(list.Handle, 0x0115 /* WM_VSCROLL */, (IntPtr)7 /* SB_BOTTOM */, IntPtr.Zero);
        var bottom = -list.AutoScrollPosition.Y;
        Assert.Equal(list.VerticalScroll.Maximum - list.VerticalScroll.LargeChange + 1, bottom);
        Assert.False(list.IsSmoothScrolling);
        list.AdvanceScrolling(TimeSpan.FromSeconds(1));
        Assert.Equal(bottom, -list.AutoScrollPosition.Y);
        SendWheel(list, -120);
        Assert.False(list.IsSmoothScrolling);
        Assert.Equal(bottom, -list.AutoScrollPosition.Y);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void WheelOverVolumeSlider_DoesNotScrollSoundListOrChangeOtherSounds() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var sounds = Enumerable.Range(1, 12).Select(index => new SoundDefinition { Name = $"音源 {index}", Volume = 0.5F }).ToList();
        manager.Current.Sounds.AddRange(sounds);
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        Application.DoEvents();
        var list = Descendants(form).OfType<ThemedFlowLayoutPanel>().Single();
        var slider = Descendants(list.Controls[0]).OfType<TrackBar>().Single();
        slider.Focus();
        var before = sounds[0].Volume;
        SendWheel(slider, -120);
        Assert.True(sounds[0].Volume < before);
        Assert.Equal(0, -list.AutoScrollPosition.Y);
        Assert.False(list.IsSmoothScrolling);
        Assert.All(sounds.Skip(1), sound => Assert.Equal(0.5F, sound.Volume));
        var numeric = Descendants(list.Controls[0]).OfType<NumericUpDown>()
            .Single(control => control.Minimum == 0 && control.Maximum == 100);
        numeric.Focus();
        before = sounds[0].Volume;
        SendWheel(numeric, -120);
        Assert.True(sounds[0].Volume < before);
        Assert.Equal(0, -list.AutoScrollPosition.Y);
        Assert.False(list.IsSmoothScrolling);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    private static void SendWheel(Control control, int delta)
    {
        var point = control.PointToScreen(new Point(10, 10));
        var coordinates = (IntPtr)((point.Y << 16) | (point.X & 0xFFFF));
        SendMessage(control.Handle, 0x020A /* WM_MOUSEWHEEL */, (IntPtr)(delta << 16), coordinates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsAndTriggerCheckBoxes_UpdateFromNativeClicks(bool darkMode) => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        manager.Current.Settings.DarkMode = darkMode;
        var sound = new SoundDefinition();
        var trigger = new TriggerDefinition { TargetSoundId = sound.Id };
        manager.Current.Sounds.Add(sound);
        manager.Current.Triggers.Add(trigger);
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Opacity = 1;
        form.TopMost = true;
        form.Show();
        Application.DoEvents();
        using (var settings = new SettingsDialog(manager.Current.Settings, [AudioDeviceInfo.Default]) { ShowInTaskbar = false })
        {
            UiTheme.Apply(settings, darkMode);
            settings.Show(form);
            Application.DoEvents();
            var checks = Descendants(settings).OfType<CheckBox>().ToArray();
            Func<bool>[] values = [() => settings.BackgroundInput, () => settings.SilentLoop,
                () => settings.FadeOutOnStop, () => settings.FadeOutOnStopAll, () => settings.DarkMode];
            Assert.Equal(values.Length, checks.Length);
            for (var index = 0; index < checks.Length; index++)
            {
                var before = values[index]();
                ClickCheckBox(checks[index]);
                Assert.Equal(!before, values[index]());
                ClickCheckBox(checks[index]);
                Assert.Equal(before, values[index]());
            }

            settings.Close();
        }

        var editedTrigger = new TriggerDefinition { TargetSoundId = sound.Id };
        using (var dialog = new TriggerDialog(editedTrigger, [sound]) { ShowInTaskbar = false })
        {
            UiTheme.Apply(dialog, darkMode);
            dialog.Show(form);
            Application.DoEvents();
            ClickCheckBox(Descendants(dialog).OfType<CheckBox>().Single());
            Descendants(dialog).OfType<Button>().Single(button => button.Text == "OK").PerformClick();
            Assert.Equal(DialogResult.OK, dialog.DialogResult);
            Assert.False(editedTrigger.Enabled);
        }

        var tabs = Descendants(form).OfType<TabControl>().Single();
        tabs.SelectedIndex = 1;
        Application.DoEvents();
        var grid = Descendants(form).OfType<DataGridView>().Single();
        var bounds = grid.GetCellDisplayRectangle(0, 0, false);
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        ClickControl(grid, center);
        Assert.False(trigger.Enabled);
        ClickControl(grid, center);
        Assert.True(trigger.Enabled);
        form.CloseWithoutSavePromptForSmokeTest();
        UiTheme.ConfigureApplicationColors(false);
    });

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static void CaptureSnapshot(Form form, string name)
    {
        var snapshotDirectory = Environment.GetEnvironmentVariable("PONDASHI_UI_SNAPSHOT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(snapshotDirectory))
        {
            return;
        }

        Directory.CreateDirectory(snapshotDirectory);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(snapshotDirectory, name), System.Drawing.Imaging.ImageFormat.Png);
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);

    private static MainForm CreateForm(ProjectManager manager, AudioEngine engine, string logDirectory) =>
        new(manager, engine, new AudioDeviceManager(), new TriggerEngine(), new NullLogger(), logDirectory)
        { ShowInTaskbar = false, Opacity = 0, Font = UiTheme.CreateFont(10F) };

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
            try
            {
                Application.EnableVisualStyles();
                action();
            }
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

    [Fact]
    public void EmergencyMenu_StopsImmediatelyAndDiscardsPreviouslyQueuedBackgroundKey() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var wavePath = directory.GetPath("emergency.wav");
        using (var writer = new WaveFileWriter(wavePath, new WaveFormat(44100, 16, 1)))
        {
            writer.Write(new byte[8820], 0, 8820);
        }
        var sound = new SoundDefinition { Key = "A", File = wavePath, FadeOut = new FadeSettings { Enabled = true, Value = 10 } };
        manager.Current.Sounds.Add(sound);
        manager.Current.Settings.FadeOutOnStopAll = true;
        manager.Current.Settings.BackgroundInput = true;
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        Application.DoEvents();
        engine.PlayAsync(sound, wavePath).GetAwaiter().GetResult();
        var starts = 0;
        engine.PlaybackStarted += (_, _) => starts++;
        var dispatcher = (ShortcutDispatcher)typeof(MainForm).GetField("_shortcutDispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var held = (HashSet<Keys>)typeof(ShortcutDispatcher).GetField("_pressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dispatcher)!;
        held.Add(Keys.B);
        Method("OnGlobalKeyChanged").Invoke(form, [null, new GlobalKeyEventArgs(Keys.A, true)]);
        var emergency = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>()
            .SelectMany(item => item.DropDownItems.OfType<ToolStripMenuItem>())
            .Single(item => item.Text?.StartsWith("緊急強制停止", StringComparison.Ordinal) == true);
        Assert.Equal(Keys.None, emergency.ShortcutKeys);
        emergency.PerformClick();
        Application.DoEvents();
        Assert.Equal(0, starts);
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.True(manager.Current.Settings.FadeOutOnStopAll);
        Assert.Contains(Keys.B, held);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void CollapsedSoundCards_RecordConstructionCost() => RunOnSta(() =>
    {
        using var warmup = new SoundCard(new SoundDefinition());
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = System.Diagnostics.Stopwatch.StartNew();
        var cards = Enumerable.Range(0, 200).Select(index => new SoundCard(new SoundDefinition { Name = $"音源{index}" })).ToArray();
        try
        {
            var count = cards.Sum(card => Descendants(card).Count());
            Console.WriteLine($"CollapsedCards count=200 controls={count} allocated_bytes={GC.GetAllocatedBytesForCurrentThread() - allocatedBefore} elapsed_ms={started.Elapsed.TotalMilliseconds:F1}");
            Assert.True(count <= 4000, $"折りたたみカードのコントロールが多すぎます: {count}");
            Assert.All(cards, card =>
            {
                Assert.False(card.DetailsCreated);
                Assert.DoesNotContain(Descendants(card).OfType<TextBox>(), editor => editor.Parent is not NumericUpDown);
            });
        }
        finally
        {
            foreach (var card in cards) { card.Dispose(); }
        }
    });

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
