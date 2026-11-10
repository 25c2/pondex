using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Input;
using PondashiEX.Project;
using PondashiEX.Trigger;
using PondashiEX.UI;
using PondashiEX.Utility;

namespace PondashiEX.Tests;

[Collection(WinFormsTestGroup.Name)]
public sealed class MainFormRegressionTests
{
    [Fact]
    public void DetailedLogging_SettingsApplyImmediatelyAndFollowProjectSwitches() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var logger = new NullLogger();
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path, logger);
        form.Show();
        Assert.False(logger.DetailedLoggingEnabled);
        Exception? callbackFailure = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            var dialog = form.OwnedForms.OfType<SettingsDialog>().SingleOrDefault();
            if (dialog is null) { return; }
            timer.Stop();
            try
            {
                Assert.False(dialog.DetailedLogging);
                Descendants(dialog).OfType<CheckBox>().Single(check => check.Text == "詳細ログを記録").Checked = true;
                Descendants(dialog).OfType<Button>().Single(button => button.Text == "OK").PerformClick();
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
                dialog.DialogResult = DialogResult.Cancel;
                dialog.Close();
            }
        };
        timer.Start();
        PumpUntil((Task)Method("ShowSettings").Invoke(form, null)!);
        Assert.Null(callbackFailure);
        Assert.True(manager.Current.Settings.DetailedLogging);
        Assert.True(manager.IsDirty);
        Assert.True(logger.DetailedLoggingEnabled);

        manager.New();
        Method("ApplyProjectSettings").Invoke(form, null);
        Assert.False(logger.DetailedLoggingEnabled);
        manager.Current.Settings.DetailedLogging = true;
        Method("ApplyProjectSettings").Invoke(form, null);
        Assert.True(logger.DetailedLoggingEnabled);
        using var restoredSettings = new SettingsDialog(manager.Current.Settings, [AudioDeviceInfo.Default]);
        Assert.True(restoredSettings.DetailedLogging);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void MasterTypedValue_IsCommittedBeforeDirectSave() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var path = directory.GetPath("master.pondex");
        manager.SaveAsAsync(path).GetAwaiter().GetResult();
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        var numeric = (NumericUpDown)typeof(MainForm).GetField("_masterVolumeValue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        numeric.Focus();
        numeric.Text = "37";
        var save = (Task<bool>)Method("SaveProjectAsync").Invoke(form, [false])!;
        PumpUntil(save);
        Assert.True(save.GetAwaiter().GetResult());
        Assert.Equal(0.37F, new ProjectSerializer().LoadAsync(path).GetAwaiter().GetResult().Settings.MasterVolume);
        Assert.False(manager.IsDirty);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void PlaybackSettings_BoostIsOptInAndLimitsMainVolumeImmediately() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        var path = directory.GetPath("boost.pondex");
        manager.Current.Sounds.Add(new SoundDefinition { File = directory.GetPath("quiet.wav") });
        var initialSave = manager.SaveAsAsync(path);
        PumpUntil(initialSave);
        initialSave.GetAwaiter().GetResult();
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        var master = (NumericUpDown)typeof(MainForm).GetField("_masterVolumeValue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var card = Descendants(form).OfType<SoundCard>().Single();
        var numeric = Descendants(card).OfType<NumericUpDown>().Single();
        var slider = Descendants(card).OfType<TrackBar>().Single();
        Assert.Equal(100m, master.Maximum);
        Assert.Equal(100m, numeric.Maximum);
        Assert.Equal(100, slider.Maximum);
        card.EnsureDetailsCreated();
        typeof(SoundCard).GetMethod("ShowPlaybackSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, null);
        var dialog = form.OwnedForms.OfType<PlaybackSettingsDialog>().Single();
        var permission = Descendants(dialog).OfType<CheckBox>().Single();
        Assert.False(permission.Checked);
        permission.Checked = true;
        Assert.True(card.Sound.AllowVolumeBoost);
        Assert.Equal(200m, numeric.Maximum);
        Assert.Equal(200, slider.Maximum);
        Assert.Equal(100m, numeric.Value);
        numeric.Value = 175;
        var save = (Task<bool>)Method("SaveProjectAsync").Invoke(form, [false])!;
        PumpUntil(save);
        Assert.True(save.GetAwaiter().GetResult());
        var load = Task.Run(() => new ProjectSerializer().LoadAsync(path));
        PumpUntil(load);
        var restored = load.GetAwaiter().GetResult();
        Assert.True(Assert.Single(restored.Sounds).AllowVolumeBoost);
        Assert.Equal(1.75F, restored.Sounds[0].Volume);
        permission.Checked = false;
        Assert.False(card.Sound.AllowVolumeBoost);
        Assert.Equal(1F, card.Sound.Volume);
        Assert.Equal(100m, numeric.Maximum);
        Assert.Equal(100m, numeric.Value);
        Assert.Equal(100, slider.Maximum);
        permission.Checked = true;
        numeric.Value = 75;
        permission.Checked = false;
        Assert.Equal(0.75F, card.Sound.Volume);
        Assert.Equal(75m, numeric.Value);
        numeric.ContextMenuStrip!.Items[0].PerformClick();
        Assert.Equal(1F, card.Sound.Volume);
        Assert.Equal(100m, numeric.Value);
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void HeaderResize_ActualControlRectanglesStayWithinParent() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        Application.DoEvents();
        var master = Assert.Single(Descendants(form).OfType<TrackBar>());
        var header = (TableLayoutPanel)master.Parent!.Parent!;
        var root = (TableLayoutPanel)header.Parent!;
        root.Dock = DockStyle.None;
        ReadOnlySpan<int> widths = [900, 1180, 1366, 500, 1500, 900];
        foreach (var width in widths)
        {
            root.Size = new Size(width, 700);
            Application.DoEvents();
            foreach (var control in Descendants(header).Where(control => control is Button or CheckBox or TrackBar or NumericUpDown or Label))
            {
                var bounds = header.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                Assert.True(header.ClientRectangle.Contains(bounds), $"幅{width}: {control.Text} {bounds}, header={header.ClientRectangle}");
                Assert.True(control.Height > 12, $"高さ不足: {control.Text}");
            }
            Assert.Contains(Descendants(header).OfType<Label>(), label => label.Text is "現在時刻" or "ストップウォッチ");
        }
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Theory]
    [InlineData(1F, 900)]
    [InlineData(1F, 1366)]
    [InlineData(1.25F, 1366)]
    [InlineData(1.5F, 1366)]
    [InlineData(1.5F, 1920)]
    [InlineData(2F, 1366)]
    [InlineData(2F, 1920)]
    public void HeaderMaster_FontScalingKeepsTextAndNumericEditorVisible(float fontScale, int width) => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        var numeric = (NumericUpDown)typeof(MainForm).GetField("_masterVolumeValue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var panel = (TableLayoutPanel)numeric.Parent!;
        var header = (TableLayoutPanel)panel.Parent!;
        var root = (TableLayoutPanel)header.Parent!;
        root.Dock = DockStyle.None;
        var originalFonts = Descendants(header).Select(control => (Control: control, Font: control.Font)).ToArray();
        var fonts = new List<Font>();
        try
        {
            foreach (var entry in originalFonts)
            {
                var font = new Font(entry.Font.FontFamily, entry.Font.SizeInPoints * fontScale, entry.Font.Style);
                fonts.Add(font);
                entry.Control.Font = font;
            }
            root.Size = new Size(width, 900);
            Application.DoEvents();
            ReadOnlySpan<string> captions = ["MASTER", "%"];
            foreach (var text in captions)
            {
                var label = Descendants(panel).OfType<Label>().Single(control => control.Text == text);
                var singleLine = TextRenderer.MeasureText(text, label.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                Assert.True(label.ClientSize.Width >= singleLine.Width,
                    $"{fontScale}x 幅{width}: {text} needs {singleLine}, actual {label.ClientSize}");
                Assert.True(label.ClientSize.Height >= singleLine.Height);
                Assert.True(panel.ClientRectangle.Contains(label.Bounds));
            }
            var edit = numeric.Controls.OfType<TextBoxBase>().Single();
            var number = TextRenderer.MeasureText("100", edit.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            Assert.True(edit.ClientSize.Width >= number.Width + 4,
                $"{fontScale}x 幅{width}: 100 needs {number.Width + 4}, actual editor width {edit.ClientSize.Width}");
            Assert.True(panel.ClientRectangle.Contains(numeric.Bounds));
            var slider = Descendants(panel).OfType<TrackBar>().Single();
            Assert.True(slider.Width >= (int)(80 * slider.DeviceDpi / 96F));
            Assert.True(panel.ClientRectangle.Contains(slider.Bounds));
            Assert.True(header.ClientRectangle.Contains(panel.Bounds));
            CaptureSnapshot(form, $"master-{fontScale}-{width}.png");
        }
        finally
        {
            form.CloseWithoutSavePromptForSmokeTest();
            foreach (var font in fonts) { font.Dispose(); }
        }
    });

    [Theory]
    [InlineData(1F)]
    [InlineData(1.5F)]
    [InlineData(2F)]
    public void EmergencyStatus_UsesFullHeightNativeButtonsAndHidesResumeUntilStopped(float fontScale) => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        var status = Descendants(form).OfType<StatusStrip>().Single();
        using var font = UiTheme.CreateFont(10F * fontScale);
        status.Font = font;
        var resume = status.Items.OfType<ToolStripButton>().Single(item => item.Text == "自動イベント再開");
        Assert.False(resume.Visible);
        Assert.DoesNotContain(status.Items.Cast<ToolStripItem>(), item => item is ToolStripControlHost);
        Method("EmergencyStopAll").Invoke(form, null);
        Application.DoEvents();
        Assert.True(resume.Visible);
        foreach (var button in status.Items.OfType<ToolStripButton>())
        {
            var preferred = button.GetPreferredSize(Size.Empty);
            Assert.True(button.Height >= preferred.Height,
                $"{fontScale}x: {button.Text} needs {preferred.Height}, actual {button.Height}");
            Assert.True(status.ClientRectangle.Contains(button.Bounds));
            var text = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            Assert.True(button.Width >= text.Width);
        }
        resume.PerformClick();
        Assert.False(resume.Visible);
        form.CloseWithoutSavePromptForSmokeTest();
    });

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
        ReadOnlySpan<int> logicalWidths = [900, 1100, 1249, 1250, 1500, 1249, 900, 1500];
        foreach (var logicalWidth in logicalWidths)
        {
            var compact = logicalWidth < 1250;
            var width = (int)(logicalWidth * header.DeviceDpi / 96F);
            header.Size = new Size(width, (int)((compact ? 236 : 104) * header.DeviceDpi / 96F));
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
            WinFormsTest.WithNeutralKeyboard(() =>
            {
                SendMessage(check.Handle, 0x0100 /* WM_KEYDOWN */, (IntPtr)Keys.Space, IntPtr.Zero);
                SendMessage(check.Handle, 0x0101 /* WM_KEYUP */, (IntPtr)Keys.Space, IntPtr.Zero);
            });
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
        form.Activate();
        Application.DoEvents();
        Assert.True(slider.Focus(), "音量スライダーにフォーカスを設定できませんでした。");
        Application.DoEvents();
        var before = sounds[0].Volume;
        SendWheel(slider, -120);
        Assert.True(sounds[0].Volume < before,
            $"ホイール後: sound={sounds[0].Volume}, before={before}, slider={slider.Value}, focused={slider.Focused}, modifiers={Control.ModifierKeys}, buttons={Control.MouseButtons}, lines={SystemInformation.MouseWheelScrollLines}");
        Assert.Equal(0, -list.AutoScrollPosition.Y);
        Assert.False(list.IsSmoothScrolling);
        Assert.All(sounds.Skip(1), sound => Assert.Equal(0.5F, sound.Volume));
        var numeric = Descendants(list.Controls[0]).OfType<NumericUpDown>()
            .Single(control => control.Minimum == 0 && control.Maximum == 100);
        Assert.True(numeric.Focus(), "音量数値欄にフォーカスを設定できませんでした。");
        Application.DoEvents();
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
        WinFormsTest.WithNeutralKeyboard(() =>
            SendMessage(control.Handle, 0x020A /* WM_MOUSEWHEEL */, (IntPtr)(delta << 16), coordinates));
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
                () => settings.FadeOutOnStop, () => settings.FadeOutOnStopAll, () => settings.DarkMode, () => settings.DetailedLogging];
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

    private static MainForm CreateForm(ProjectManager manager, AudioEngine engine, string logDirectory, IAppLogger? logger = null) =>
        new(manager, engine, new AudioDeviceManager(() => new AudioDeviceSnapshot(
            [AudioDeviceInfo.Default, new AudioDeviceInfo(0, "Test output", "test-output")], new DefaultOutputIdentity(0, "test-endpoint"))), new TriggerEngine(), logger ?? new NullLogger(), logDirectory)
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

    private static void RunOnSta(Action action) => WinFormsTest.Run(action);

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

    [Fact]
    public void ProjectRuntimeReset_StopsStopwatchAndClearsPendingNaturalEvents() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        var triggerEngine = (TriggerEngine)typeof(MainForm).GetField("_triggerEngine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var stopwatch = (System.Diagnostics.Stopwatch)typeof(MainForm).GetField("_stopwatch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var source = Guid.NewGuid();
        manager.Current.Triggers.Add(new TriggerDefinition
        {
            TriggerType = TriggerType.SoundEnded,
            Parameters = new TriggerParameters { SourceSoundId = source }
        });
        triggerEngine.SetTriggers(manager.Current.Triggers);
        triggerEngine.OnSoundEnded(source, true);
        stopwatch.Start();
        Method("ResetProjectRuntime").Invoke(form, null);
        Assert.False(stopwatch.IsRunning);
        Assert.Equal(TimeSpan.Zero, stopwatch.Elapsed);
        Assert.Empty(triggerEngine.Tick(DateTimeOffset.Now, TimeSpan.Zero, false));
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Fact]
    public void PlaybackDialogs_AreModelessUniqueAndCloseWithCard() => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        manager.Current.Sounds.Add(new SoundDefinition { Name = "one" });
        manager.Current.Sounds.Add(new SoundDefinition { Name = "two" });
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        form.Show();
        var cards = Descendants(form).OfType<SoundCard>().ToArray();
        var open = typeof(SoundCard).GetMethod("ShowPlaybackSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var card in cards)
        {
            Descendants(card).OfType<Button>().Single(button => button.Text.StartsWith("詳細", StringComparison.Ordinal)).PerformClick();
            open.Invoke(card, null);
            open.Invoke(card, null);
        }
        Application.DoEvents();
        Assert.Equal(2, form.OwnedForms.OfType<PlaybackSettingsDialog>().Count());
        Assert.All(form.OwnedForms, dialog => Assert.False(dialog.Modal));
        Assert.True(form.Enabled);
        var dialogs = form.OwnedForms.ToArray();
        foreach (var card in cards) { card.Dispose(); }
        Assert.All(dialogs, dialog => Assert.True(dialog.IsDisposed));
        form.CloseWithoutSavePromptForSmokeTest();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClockDisplay_UsesHeaderContrastInBothModes(bool dark) => RunOnSta(() =>
    {
        using var directory = new TestDirectory();
        using var manager = new ProjectManager(new ProjectSerializer());
        manager.Current.Settings.DarkMode = dark;
        manager.Current.Project.ThemeColor = "#FFFFFF";
        using var engine = CreateEngine();
        using var form = CreateForm(manager, engine, directory.Path);
        var clock = (Label)typeof(MainForm).GetField("_clockLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        Method("UpdateClockDisplay").Invoke(form, null);
        Assert.NotEqual(clock.BackColor.ToArgb(), clock.ForeColor.ToArgb());
        typeof(MainForm).GetField("_stopwatchMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
        Method("UpdateClockDisplay").Invoke(form, null);
        Assert.NotEqual(clock.BackColor.ToArgb(), clock.ForeColor.ToArgb());
        form.CloseWithoutSavePromptForSmokeTest();
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
        public bool DetailedLoggingEnabled { get; set; }

        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
