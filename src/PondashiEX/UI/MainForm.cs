using System.Diagnostics;
using System.Globalization;
using PondashiEX.Audio;
using PondashiEX.Diagnostics;
using PondashiEX.Input;
using PondashiEX.Project;
using PondashiEX.Trigger;
using PondashiEX.Utility;

namespace PondashiEX.UI;

public sealed class MainForm : Form
{
    private readonly ProjectManager _projectManager;
    private readonly IAudioEngine _audioEngine;
    private readonly AudioDeviceManager _deviceManager;
    private readonly TriggerEngine _triggerEngine;
    private readonly IAppLogger _logger;
    private readonly string _logDirectory;
    private readonly RecentProjectStore _recentProjects;
    private readonly Stopwatch _stopwatch = new();
    private readonly Dictionary<Guid, SoundCard> _soundCards = [];
    private readonly GlobalKeyboardHook _globalHook = new();
    private readonly ShortcutDispatcher _shortcutDispatcher;
    private readonly ShortcutMessageFilter _messageFilter;
    private readonly System.Windows.Forms.Timer _clockTimer;
    private readonly FlowLayoutPanel _soundList;
    private readonly TriggerPanel _triggerPanel;
    private readonly Label _projectLabel;
    private readonly Label _deviceLabel;
    private readonly Label _clockLabel;
    private readonly Label _clockModeLabel;
    private readonly ToolStripStatusLabel _statusLabel;
    private readonly ToolStripMenuItem _recentMenu;
    private readonly TrackBar _masterVolume;
    private readonly CheckBox _masterMute;
    private bool _stopwatchMode;
    private bool _closingAfterSave;
    private bool _applyingProjectSettings;
    private bool _cleanupCompleted;
    private DateTimeOffset _nextDeviceAvailabilityCheck = DateTimeOffset.MinValue;

    public MainForm(
        ProjectManager projectManager,
        IAudioEngine audioEngine,
        AudioDeviceManager deviceManager,
        TriggerEngine triggerEngine,
        IAppLogger logger,
        string logDirectory)
    {
        _projectManager = projectManager;
        _audioEngine = audioEngine;
        _deviceManager = deviceManager;
        _triggerEngine = triggerEngine;
        _logger = logger;
        _logDirectory = Path.GetFullPath(logDirectory);
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PondashiEX");
        _recentProjects = new RecentProjectStore(Path.Combine(settingsDirectory, "recent-projects.json"));

        Text = "効果音ポン出しくんEX";
        MinimumSize = new Size(500, 320);
        ClientSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        DpiWindowHelper.Attach(this);
        KeyPreview = false;
        AllowDrop = true;

        var menu = BuildMenu(out _recentMenu);
        MainMenuStrip = menu;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.Controls.Add(menu, 0, 0);

        var header = BuildHeader(
            out _projectLabel,
            out _deviceLabel,
            out _clockLabel,
            out _clockModeLabel,
            out _masterVolume,
            out _masterMute);
        root.Controls.Add(header, 0, 1);

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 10) };
        var soundsTab = new TabPage("音源") { Padding = new Padding(4) };
        _soundList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.FromArgb(224, 228, 232),
            Padding = new Padding(4)
        };
        soundsTab.Controls.Add(_soundList);
        var triggersTab = new TabPage("イベントトリガ") { Padding = new Padding(4) };
        _triggerPanel = new TriggerPanel { Dock = DockStyle.Fill };
        _triggerPanel.DefinitionsChanged += (_, _) =>
        {
            _triggerEngine.SetTriggers(_projectManager.Current.Triggers);
            _projectManager.MarkDirty();
        };
        triggersTab.Controls.Add(_triggerPanel);
        tabs.TabPages.Add(soundsTab);
        tabs.TabPages.Add(triggersTab);
        root.Controls.Add(tabs, 0, 2);

        var statusStrip = new StatusStrip { SizingGrip = false };
        _statusLabel = new ToolStripStatusLabel("準備完了") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusStrip.Items.Add(_statusLabel);
        root.Controls.Add(statusStrip, 0, 3);
        Controls.Add(root);

        _shortcutDispatcher = new ShortcutDispatcher(
            new ShortcutManager(),
            () => _projectManager.Current.Sounds,
            IsEditingText,
            ExecuteShortcut);
        _messageFilter = new ShortcutMessageFilter(
            _shortcutDispatcher.OnKeyChanged,
            () => !_projectManager.Current.Settings.BackgroundInput);
        Application.AddMessageFilter(_messageFilter);
        _globalHook.KeyChanged += OnGlobalKeyChanged;

        _clockTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _clockTimer.Tick += (_, _) => UpdateClockAndTriggers();
        _clockTimer.Start();

        _projectManager.ProjectChanged += OnProjectChanged;
        _audioEngine.StateChanged += OnSoundStateChanged;
        _audioEngine.PlaybackStarted += OnPlaybackStarted;
        _audioEngine.PlaybackEnded += OnPlaybackEnded;
        _audioEngine.PlaybackFailed += OnPlaybackFailed;
        _audioEngine.OutputDeviceFallback += OnOutputDeviceFallback;
        _triggerEngine.TriggerFired += OnTriggerFired;

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        Resize += (_, _) => ResizeSoundCards();
        FormClosing += OnFormClosing;
        FormClosed += OnFormClosed;

        ApplyProjectSettings();
        RebuildSoundCards();
        RefreshRecentMenu();
    }

    private MenuStrip BuildMenu(out ToolStripMenuItem recentMenu)
    {
        var menu = new MenuStrip { Dock = DockStyle.Fill };
        var file = new ToolStripMenuItem("ファイル(&F)");
        var newItem = new ToolStripMenuItem("新規(&N)", null, async (_, _) => await NewProjectAsync());
        var openItem = new ToolStripMenuItem("開く(&O)…", null, async (_, _) => await OpenWithDialogAsync());
        var saveItem = new ToolStripMenuItem("保存(&S)", null, async (_, _) => await SaveProjectAsync(forceSaveAs: false));
        var saveAsItem = new ToolStripMenuItem("名前を付けて保存(&A)…", null, async (_, _) => await SaveProjectAsync(forceSaveAs: true));
        var addItem = new ToolStripMenuItem("音源を追加…", null, (_, _) => AddSoundsWithDialog());
        recentMenu = new ToolStripMenuItem("最近使ったプロジェクト");
        file.DropDownItems.AddRange([newItem, openItem, saveItem, saveAsItem, new ToolStripSeparator(), addItem, recentMenu]);

        var operations = new ToolStripMenuItem("操作(&O)");
        var stopAllItem = new ToolStripMenuItem("全停止(&R)", null, (_, _) => StopAll())
        {
            ShortcutKeyDisplayString = "Ctrl+R"
        };
        operations.DropDownItems.Add(stopAllItem);

        var settings = new ToolStripMenuItem("設定(&S)", null, (_, _) => ShowSettings());
        var diagnostics = new ToolStripMenuItem("動作診断(&D)…", null, async (_, _) => await RunSelfTestAsync());
        menu.Items.AddRange([file, operations, settings, diagnostics]);
        return menu;
    }

    private TableLayoutPanel BuildHeader(
        out Label projectLabel,
        out Label deviceLabel,
        out Label clockLabel,
        out Label clockModeLabel,
        out TrackBar masterVolume,
        out CheckBox masterMute)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 6, 8, 6),
            ColumnCount = 6,
            RowCount = 2,
            BackColor = Color.FromArgb(36, 40, 44)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        projectLabel = HeaderLabel("プロジェクト: 新しいプロジェクト", 11, FontStyle.Bold);
        deviceLabel = HeaderLabel("出力: Windows 既定", 9, FontStyle.Regular);
        clockModeLabel = HeaderLabel("現在時刻", 9, FontStyle.Bold);
        clockLabel = HeaderLabel("00:00:00", 22, FontStyle.Bold);
        clockLabel.TextAlign = ContentAlignment.MiddleCenter;

        var addButton = HeaderButton("＋ 音源追加", Color.FromArgb(225, 235, 245));
        var stopAllButton = HeaderButton("■ 全停止", Color.FromArgb(220, 65, 65));
        stopAllButton.ForeColor = Color.White;
        stopAllButton.Font = new Font(Font.FontFamily, 14, FontStyle.Bold);
        var modeButton = HeaderButton("時計 / SW切替", SystemColors.Control);
        var startButton = HeaderButton("Start / Pause", SystemColors.Control);
        var resetButton = HeaderButton("Reset", SystemColors.Control);

        var masterVolumeControl = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Value = 100,
            Dock = DockStyle.Fill
        };
        masterVolume = masterVolumeControl;
        var masterMuteControl = new CheckBox
        {
            Text = "MUTE",
            ForeColor = Color.White,
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        masterMute = masterMuteControl;
        var masterPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        masterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        masterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        masterPanel.Controls.Add(HeaderLabel("MASTER", 8, FontStyle.Bold), 0, 0);
        masterPanel.Controls.Add(masterVolumeControl, 1, 0);

        header.Controls.Add(projectLabel, 0, 0);
        header.Controls.Add(deviceLabel, 0, 1);
        header.Controls.Add(masterPanel, 1, 0);
        header.Controls.Add(masterMuteControl, 1, 1);
        header.Controls.Add(clockModeLabel, 2, 0);
        header.Controls.Add(clockLabel, 2, 1);
        header.Controls.Add(modeButton, 3, 0);
        header.Controls.Add(startButton, 3, 1);
        header.Controls.Add(resetButton, 4, 1);
        header.Controls.Add(addButton, 4, 0);
        header.Controls.Add(stopAllButton, 5, 0);
        header.SetRowSpan(stopAllButton, 2);

        addButton.Click += (_, _) => AddSoundsWithDialog();
        stopAllButton.Click += (_, _) => StopAll();
        modeButton.Click += (_, _) =>
        {
            _stopwatchMode = !_stopwatchMode;
            UpdateClockDisplay();
        };
        startButton.Click += (_, _) =>
        {
            _stopwatchMode = true;
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
            }
            else
            {
                _stopwatch.Start();
            }

            UpdateClockDisplay();
        };
        resetButton.Click += (_, _) =>
        {
            _stopwatch.Reset();
            _stopwatchMode = true;
            UpdateClockDisplay();
        };
        masterVolumeControl.ValueChanged += (_, _) =>
        {
            if (_applyingProjectSettings)
            {
                return;
            }

            var volume = masterVolumeControl.Value / 100.0f;
            _audioEngine.MasterVolume = volume;
            _projectManager.Current.Settings.MasterVolume = volume;
            _projectManager.MarkDirty();
        };
        masterMuteControl.CheckedChanged += (_, _) =>
        {
            if (_applyingProjectSettings)
            {
                return;
            }

            _audioEngine.MasterMute = masterMuteControl.Checked;
            _projectManager.Current.Settings.MasterMute = masterMuteControl.Checked;
            _projectManager.MarkDirty();
        };
        return header;
    }

    private static Label HeaderLabel(string text, float size, FontStyle style) => new()
    {
        Text = text,
        ForeColor = Color.White,
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font(SystemFonts.DefaultFont.FontFamily, size, style)
    };

    private static Button HeaderButton(string text, Color color) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        BackColor = color,
        UseVisualStyleBackColor = false,
        Margin = new Padding(4),
        Font = new Font(SystemFonts.DefaultFont.FontFamily, 9, FontStyle.Bold)
    };

    private async Task NewProjectAsync()
    {
        if (!await EnsureCanReplaceProjectAsync())
        {
            return;
        }

        _audioEngine.StopAll();
        _projectManager.New();
        ApplyProjectSettings();
        RebuildSoundCards();
        SetStatus("新しいプロジェクトを作成しました。", false);
    }

    private async Task OpenWithDialogAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "PondashiEXプロジェクト (*.pondex)|*.pondex|すべてのファイル (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await OpenProjectAsync(dialog.FileName);
        }
    }

    private async Task OpenProjectAsync(string path)
    {
        if (!await EnsureCanReplaceProjectAsync())
        {
            return;
        }

        try
        {
            _audioEngine.StopAll();
            await _projectManager.OpenAsync(path);
            _recentProjects.Add(path);
            _logger.Info($"プロジェクトを開きました: {path}");
            ApplyProjectSettings();
            RebuildSoundCards();
            RefreshRecentMenu();
            if (_projectManager.Current.LoadWarnings.Count > 0)
            {
                var warningText = string.Join(Environment.NewLine, _projectManager.Current.LoadWarnings.Take(10));
                _logger.Warning($"プロジェクトの一部を修復またはスキップしました:{Environment.NewLine}{warningText}");
                MessageBox.Show(
                    this,
                    $"プロジェクトの一部を読み込めませんでした。正常な項目は利用できます。\n\n{warningText}",
                    "プロジェクト読込警告",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                SetStatus($"警告付きで開きました（{_projectManager.Current.LoadWarnings.Count}件）。", true);
            }
            else
            {
                SetStatus("プロジェクトを開きました。", false);
            }
        }
        catch (Exception exception)
        {
            _logger.Error($"プロジェクトを開けません: {path}", exception);
            MessageBox.Show(this, exception.Message, "プロジェクト読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task<bool> SaveProjectAsync(bool forceSaveAs)
    {
        try
        {
            if (forceSaveAs || _projectManager.CurrentPath is null)
            {
                using var dialog = new SaveFileDialog
                {
                    Filter = "PondashiEXプロジェクト (*.pondex)|*.pondex",
                    DefaultExt = "pondex",
                    AddExtension = true,
                    FileName = SanitizeFileName(_projectManager.Current.Project.Name) + ".pondex"
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                await _projectManager.SaveAsAsync(dialog.FileName);
            }
            else
            {
                await _projectManager.SaveAsync();
            }

            _recentProjects.Add(_projectManager.CurrentPath!);
            _logger.Info($"プロジェクトを保存しました: {_projectManager.CurrentPath}");
            RefreshRecentMenu();
            SetStatus("保存しました。", false);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error("プロジェクトを保存できません。", exception);
            MessageBox.Show(this, exception.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void AddSoundsWithDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "対応音声 (*.wav;*.mp3;*.ogg;*.opus)|*.wav;*.mp3;*.ogg;*.opus|すべてのファイル (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddSoundFiles(dialog.FileNames);
        }
    }

    private void AddSoundFiles(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths)
        {
            if (!IsSupportedAudioFile(path))
            {
                SetStatus($"未対応形式をスキップしました: {Path.GetFileName(path)}", true);
                continue;
            }

            _projectManager.AddSound(path);
            added++;
        }

        if (added > 0)
        {
            RebuildSoundCards();
            SetStatus($"音源を{added}件追加しました。", false);
        }
    }

    private void RebuildSoundCards()
    {
        _soundList.SuspendLayout();
        try
        {
            _soundList.Controls.Clear();
            _soundCards.Clear();
            foreach (var sound in _projectManager.Current.Sounds)
            {
                var card = new SoundCard(sound);
                card.PlayRequested += async (_, _) => await PlaySoundAsync(sound.Id);
                card.StopRequested += (_, _) => _audioEngine.Stop(sound.Id);
                card.PauseRequested += (_, _) => _audioEngine.Pause(sound.Id);
                card.ResumeRequested += (_, _) => _audioEngine.Resume(sound.Id);
                card.VolumeFadeRequested += (_, eventArgs) =>
                {
                    _audioEngine.FadeVolume(sound.Id, eventArgs.TargetVolume, eventArgs.Duration);
                    _projectManager.MarkDirty();
                };
                card.DefinitionChanged += (_, _) =>
                {
                    _audioEngine.ApplyLiveSettings(sound);
                    _projectManager.MarkDirty();
                };
                card.UpdateState(_audioEngine.GetState(sound.Id));
                _soundCards[sound.Id] = card;
                _soundList.Controls.Add(card);
            }

            ResizeSoundCards();
            _triggerPanel.SetData(_projectManager.Current.Triggers, _projectManager.Current.Sounds);
            _triggerEngine.SetTriggers(_projectManager.Current.Triggers);
        }
        finally
        {
            _soundList.ResumeLayout();
        }
    }

    private void ResizeSoundCards()
    {
        var width = Math.Max(720, _soundList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 20);
        foreach (var card in _soundCards.Values)
        {
            card.Width = width;
        }
    }

    private async Task PlaySoundAsync(Guid soundId)
    {
        var sound = _projectManager.Current.Sounds.FirstOrDefault(item => item.Id == soundId);
        if (sound is null)
        {
            return;
        }

        string path;
        try
        {
            path = _projectManager.ResolveSoundPath(sound);
        }
        catch (Exception exception)
        {
            SetStatus($"パスを解決できません: {sound.Name} — {exception.Message}", true);
            return;
        }

        await _audioEngine.PlayAsync(sound, path);
    }

    private void StopAll()
    {
        _audioEngine.StopAll();
        SetStatus("すべての音源を停止しました。", false);
    }

    private void ExecuteShortcut(ShortcutCommand command)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            TryBeginInvoke(() => ExecuteShortcut(command));
            return;
        }

        switch (command.Type)
        {
            case ShortcutCommandType.Play when command.SoundId is { } playId:
                _ = PlaySoundAsync(playId);
                break;
            case ShortcutCommandType.Stop when command.SoundId is { } stopId:
                _audioEngine.Stop(stopId);
                break;
            case ShortcutCommandType.StopAll:
                StopAll();
                break;
        }
    }

    private void ShowSettings()
    {
        var devices = _deviceManager.GetOutputDevices();
        using var dialog = new SettingsDialog(_projectManager.Current.Settings, devices);
        dialog.SetProjectMetadata(_projectManager.Current.Project);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var settings = _projectManager.Current.Settings;
        var selectedDevice = devices.FirstOrDefault(
            device => device.DeviceNumber == dialog.SelectedDeviceNumber)
            ?? AudioDeviceInfo.Default;
        settings.OutputDevice = selectedDevice.PersistentId;
        settings.BackgroundInput = dialog.BackgroundInput;
        settings.SilentLoop = dialog.SilentLoop;
        _projectManager.Current.Project.Name = string.IsNullOrWhiteSpace(dialog.ProjectName)
            ? "名称未設定"
            : dialog.ProjectName;
        _projectManager.Current.Project.ThemeColor = dialog.ThemeColor;
        _projectManager.MarkDirty();
        ApplyProjectSettings();
    }

    private async Task RunSelfTestAsync()
    {
        var confirmation = MessageBox.Show(
            this,
            "WAV / MP3 / Ogg Vorbis / Ogg Opusの短いテスト音を順番に再生し、出力・バックグラウンド入力・DPI設定を診断します。\n\n再生中の音源は停止します。実行しますか？",
            "動作診断",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);
        if (confirmation != DialogResult.OK)
        {
            return;
        }

        StopAll();
        UseWaitCursor = true;
        SetStatus("動作診断を実行しています…", false);
        try
        {
            var result = await AudioSelfTest.RunAsync(_logger);
            var reportPath = AudioSelfTest.SaveReport(result, _logDirectory);
            MessageBox.Show(
                this,
                $"動作診断: {(result.Success ? "合格" : "失敗")}\n\n{result.ToReport()}\nレポート: {reportPath}",
                "動作診断",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            SetStatus(result.Success ? "動作診断に合格しました。" : "動作診断で問題を検出しました。", !result.Success);
        }
        catch (Exception exception)
        {
            _logger.Error("動作診断を実行できませんでした。", exception);
            SetStatus($"動作診断を実行できませんでした: {exception.Message}", true);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void ApplyProjectSettings()
    {
        var settings = _projectManager.Current.Settings;
        _applyingProjectSettings = true;
        try
        {
            _masterVolume.Value = (int)(Math.Clamp(settings.MasterVolume, 0, 1) * 100);
            _masterMute.Checked = settings.MasterMute;
        }
        finally
        {
            _applyingProjectSettings = false;
        }

        _audioEngine.MasterVolume = settings.MasterVolume;
        _audioEngine.MasterMute = settings.MasterMute;

        var devices = _deviceManager.GetOutputDevices();
        var selected = AudioDeviceManager.FindConfiguredDevice(devices, settings.OutputDevice);
        if (selected is null)
        {
            settings.OutputDevice = "default";
            _logger.Warning("設定された出力デバイスが見つからないため、Windows既定へフォールバックしました。");
            selected = AudioDeviceInfo.Default;
        }

        try
        {
            _audioEngine.SelectOutputDevice(selected.DeviceNumber);
            _audioEngine.SilentLoopEnabled = settings.SilentLoop;
            var activeDevice = devices.FirstOrDefault(
                device => device.DeviceNumber == _audioEngine.OutputDeviceNumber)
                ?? AudioDeviceInfo.Default;
            if (activeDevice.DeviceNumber != selected.DeviceNumber)
            {
                settings.OutputDevice = "default";
            }

            _deviceLabel.Text = $"出力: {activeDevice.Name}";
        }
        catch (Exception exception)
        {
            _logger.Error("出力デバイスを使用できません。", exception);
            _deviceLabel.Text = "出力: エラー（設定を確認）";
            SetStatus("出力デバイスを使用できません。", true);
        }

        try
        {
            if (settings.BackgroundInput)
            {
                _globalHook.Enable();
            }
            else
            {
                _globalHook.Disable();
                _shortcutDispatcher.Reset();
            }
        }
        catch (Exception exception)
        {
            settings.BackgroundInput = false;
            _globalHook.Disable();
            _logger.Error("バックグラウンド入力を有効にできません。", exception);
            SetStatus("バックグラウンド入力を開始できません。", true);
        }

        try
        {
            var themeColor = ColorTranslator.FromHtml(_projectManager.Current.Project.ThemeColor);
            BackColor = themeColor;
            ApplyHeaderContrast(themeColor);
        }
        catch
        {
            BackColor = SystemColors.Control;
        }

        UpdateProjectHeader();
    }

    private void UpdateClockAndTriggers()
    {
        UpdateClockDisplay();
        CheckSelectedDeviceAvailability();
        var fired = _triggerEngine.Tick(DateTimeOffset.Now, _stopwatch.Elapsed);
        if (fired.Count > 0)
        {
            _projectManager.MarkDirty();
            _triggerPanel.SetData(_projectManager.Current.Triggers, _projectManager.Current.Sounds);
        }
    }

    private void CheckSelectedDeviceAvailability()
    {
        var now = DateTimeOffset.Now;
        if (now < _nextDeviceAvailabilityCheck)
        {
            return;
        }

        _nextDeviceAvailabilityCheck = now.AddSeconds(2);
        var selectedDeviceNumber = _audioEngine.OutputDeviceNumber;
        if (selectedDeviceNumber < 0
            || _deviceManager.GetOutputDevices().Any(device => device.DeviceNumber == selectedDeviceNumber))
        {
            return;
        }

        try
        {
            _audioEngine.SelectOutputDevice(-1);
            _projectManager.Current.Settings.OutputDevice = "default";
            _projectManager.MarkDirty();
            _deviceLabel.Text = "出力: Windows 既定の出力（切断検出）";
            _logger.Warning($"出力デバイス{selectedDeviceNumber}の切断を検出し、Windows既定へ切り替えました。");
            SetStatus("出力デバイスの切断を検出し、Windows既定へ切り替えました。", true);
        }
        catch (Exception exception)
        {
            _logger.Error("切断された出力デバイスから復旧できませんでした。", exception);
            _deviceLabel.Text = "出力: 復旧失敗（設定を確認）";
            SetStatus("出力デバイスの切断後に復旧できませんでした。", true);
        }
    }

    private void UpdateClockDisplay()
    {
        if (_stopwatchMode)
        {
            _clockModeLabel.Text = _stopwatch.IsRunning ? "ストップウォッチ（計測中）" : "ストップウォッチ（停止）";
            _clockLabel.Text = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture);
            _clockLabel.ForeColor = _stopwatch.IsRunning ? Color.LightGreen : Color.Khaki;
        }
        else
        {
            _clockModeLabel.Text = "現在時刻";
            _clockLabel.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            _clockLabel.ForeColor = Color.White;
        }
    }

    private void OnTriggerFired(object? sender, TriggerFiredEventArgs eventArgs)
    {
        var trigger = eventArgs.Trigger;
        switch (trigger.Action)
        {
            case TriggerAction.Play:
                _ = PlaySoundAsync(trigger.TargetSoundId);
                break;
            case TriggerAction.Stop:
                _audioEngine.Stop(trigger.TargetSoundId);
                break;
            case TriggerAction.Pause:
                _audioEngine.Pause(trigger.TargetSoundId);
                break;
        }

        SetStatus($"イベントを実行しました: {trigger.Memo}", false);
    }

    private void OnSoundStateChanged(object? sender, SoundStateChangedEventArgs eventArgs)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            TryBeginInvoke(() => OnSoundStateChanged(sender, eventArgs));
            return;
        }

        if (_soundCards.TryGetValue(eventArgs.SoundId, out var card))
        {
            card.UpdateState(eventArgs.State);
        }
    }

    private void OnPlaybackFailed(object? sender, SoundErrorEventArgs eventArgs)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            TryBeginInvoke(() => OnPlaybackFailed(sender, eventArgs));
            return;
        }

        SetStatus($"{eventArgs.Message} {eventArgs.Exception.Message}", true);
    }

    private void OnOutputDeviceFallback(object? sender, OutputDeviceFallbackEventArgs eventArgs)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            TryBeginInvoke(() => OnOutputDeviceFallback(sender, eventArgs));
            return;
        }

        if (eventArgs.Recovered)
        {
            _projectManager.Current.Settings.OutputDevice = "default";
            _projectManager.MarkDirty();
            _deviceLabel.Text = "出力: Windows 既定の出力（自動復旧）";
            SetStatus("出力デバイス異常を検出し、Windows既定の出力へ切り替えました。再生中の音源は安全のため停止しました。", true);
        }
        else
        {
            _deviceLabel.Text = "出力: 復旧失敗（設定を確認）";
            SetStatus("音声出力デバイスを復旧できません。接続とWindowsの音声設定を確認してください。", true);
        }
    }

    private void UpdateProjectHeader()
    {
        var dirty = _projectManager.IsDirty ? " *" : string.Empty;
        _projectLabel.Text = $"プロジェクト: {_projectManager.Current.Project.Name}{dirty}";
        Text = $"効果音ポン出しくんEX — {_projectManager.Current.Project.Name}{dirty}";
    }

    private void RefreshRecentMenu()
    {
        _recentMenu.DropDownItems.Clear();
        var recent = _recentProjects.Load();
        if (recent.Count == 0)
        {
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem("（なし）") { Enabled = false });
            return;
        }

        foreach (var path in recent)
        {
            var item = new ToolStripMenuItem(path);
            item.Click += async (_, _) => await OpenProjectAsync(path);
            _recentMenu.DropDownItems.Add(item);
        }
    }

    private async Task<bool> EnsureCanReplaceProjectAsync()
    {
        if (!_projectManager.IsDirty)
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
            "変更を保存しますか？",
            "PondashiEX",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);
        return result switch
        {
            DialogResult.Yes => await SaveProjectAsync(forceSaveAs: false),
            DialogResult.No => true,
            _ => false
        };
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_closingAfterSave || !_projectManager.IsDirty)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (await EnsureCanReplaceProjectAsync())
        {
            _closingAfterSave = true;
            Close();
        }
    }

    private void CleanupInput()
    {
        if (_cleanupCompleted)
        {
            return;
        }

        _cleanupCompleted = true;
        _projectManager.ProjectChanged -= OnProjectChanged;
        _audioEngine.StateChanged -= OnSoundStateChanged;
        _audioEngine.PlaybackStarted -= OnPlaybackStarted;
        _audioEngine.PlaybackEnded -= OnPlaybackEnded;
        _audioEngine.PlaybackFailed -= OnPlaybackFailed;
        _audioEngine.OutputDeviceFallback -= OnOutputDeviceFallback;
        _triggerEngine.TriggerFired -= OnTriggerFired;
        _globalHook.KeyChanged -= OnGlobalKeyChanged;
        _clockTimer.Stop();
        _clockTimer.Dispose();
        Application.RemoveMessageFilter(_messageFilter);
        _globalHook.Dispose();
        _audioEngine.StopAll();
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs eventArgs) => CleanupInput();

    private void OnProjectChanged(object? sender, EventArgs eventArgs) => UpdateProjectHeader();

    private void OnPlaybackStarted(object? sender, SoundPlaybackEventArgs eventArgs) =>
        _triggerEngine.OnSoundStarted(eventArgs.SoundId);

    private void OnPlaybackEnded(object? sender, SoundPlaybackEventArgs eventArgs) =>
        _triggerEngine.OnSoundEnded(
            eventArgs.SoundId,
            eventArgs.EndReason == PlaybackEndReason.Natural);

    private void OnGlobalKeyChanged(object? sender, GlobalKeyEventArgs eventArgs)
    {
        if (_projectManager.Current.Settings.BackgroundInput)
        {
            _shortcutDispatcher.OnKeyChanged(eventArgs.KeyData, eventArgs.IsDown);
        }
    }

    private bool TryBeginInvoke(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return false;
        }

        try
        {
            BeginInvoke(action);
            return true;
        }
        catch (InvalidOperationException)
        {
            // The handle can be destroyed between the checks while the app is closing.
            return false;
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs eventArgs)
    {
        if (eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            eventArgs.Effect = DragDropEffects.Copy;
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs eventArgs)
    {
        if (eventArgs.Data?.GetData(DataFormats.FileDrop) is string[] paths)
        {
            AddSoundFiles(paths);
        }
    }

    private bool IsEditingText()
    {
        if (!ContainsFocus)
        {
            return false;
        }

        var control = FindFocusedControl(this);
        return control switch
        {
            TextBoxBase textBox => !textBox.ReadOnly,
            NumericUpDown or ComboBox => true,
            _ => false
        };
    }

    private static Control? FindFocusedControl(ContainerControl container)
    {
        var active = container.ActiveControl;
        while (active is ContainerControl nested && nested.ActiveControl is not null)
        {
            active = nested.ActiveControl;
        }

        return active;
    }

    private void SetStatus(string message, bool error)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = error ? Color.DarkRed : SystemColors.ControlText;
    }

    private void ApplyHeaderContrast(Color themeColor)
    {
        var header = Controls
            .OfType<TableLayoutPanel>()
            .SelectMany(root => root.Controls.OfType<TableLayoutPanel>())
            .FirstOrDefault(panel => panel.Contains(_projectLabel));
        if (header is null)
        {
            return;
        }

        header.BackColor = themeColor;
        var foreground = themeColor.GetBrightness() > 0.62f ? Color.Black : Color.White;
        foreach (var label in header.Controls.OfType<Label>())
        {
            label.ForeColor = foreground;
        }

        _masterMute.ForeColor = foreground;
    }

    private static bool IsSupportedAudioFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".opus", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "show" : sanitized;
    }
}
