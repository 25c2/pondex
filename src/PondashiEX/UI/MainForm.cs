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
    private readonly TabControl _tabs;
    private readonly Label _projectLabel;
    private readonly Label _deviceLabel;
    private readonly Label _clockLabel;
    private readonly Label _clockModeLabel;
    private readonly ToolStripStatusLabel _statusLabel;
    private readonly ToolStripMenuItem _recentMenu;
    private readonly ToolStripMenuItem _muteMenuItem;
    private readonly TrackBar _masterVolume;
    private readonly CheckBox _masterMute;
    private NumericUpDown _masterVolumeValue = null!;
    private Button _stopAllButton = null!;
    private readonly ToolTip _headerToolTip = new();
    private bool _stopwatchMode;
    private bool _closingAfterSave;
    private bool _applyingProjectSettings;
    private bool _cleanupCompleted;
    private bool _darkMode;
    private bool _statusIsError;
    private bool _projectOperationInProgress;
    private DateTimeOffset _nextDeviceAvailabilityCheck = DateTimeOffset.MinValue;
    private long _playbackGeneration;

    public MainForm(
        ProjectManager projectManager,
        IAudioEngine audioEngine,
        AudioDeviceManager deviceManager,
        TriggerEngine triggerEngine,
        IAppLogger logger,
        string logDirectory,
        RecentProjectStore? recentProjects = null)
    {
        UiTheme.ConfigureApplicationColors(projectManager.Current.Settings.DarkMode);
        _projectManager = projectManager;
        _audioEngine = audioEngine;
        _deviceManager = deviceManager;
        _triggerEngine = triggerEngine;
        _logger = logger;
        _logDirectory = Path.GetFullPath(logDirectory);
        _recentProjects = recentProjects ?? new RecentProjectStore(Path.Combine(_logDirectory, "recent-projects.json"));

        Text = "効果音ポン出しくんEX";
        MinimumSize = new Size(500, 320);
        ClientSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        DpiWindowHelper.Attach(this);
        KeyPreview = false;
        AllowDrop = true;

        var menu = BuildMenu(out _recentMenu, out _muteMenuItem);
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.Controls.Add(menu, 0, 0);

        var header = BuildHeader(
            out _projectLabel,
            out _deviceLabel,
            out _clockLabel,
            out _clockModeLabel,
            out _masterVolume,
            out _masterMute);
        root.Controls.Add(header, 0, 1);

        _tabs = new ThemedTabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 10),
            DrawMode = TabDrawMode.OwnerDrawFixed
        };
        var soundsTab = new TabPage("音源") { Padding = new Padding(4) };
        _soundList = new ThemedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.FromArgb(224, 228, 232),
            Padding = new Padding(4),
            AllowDrop = true,
            Tag = UiTheme.SoundListRole
        };
        _soundList.MouseDown += (_, _) => EndTextEditing();
        _soundList.DragEnter += OnDragEnter;
        _soundList.DragOver += OnDragEnter;
        _soundList.DragDrop += OnDragDrop;
        soundsTab.Controls.Add(_soundList);
        var triggersTab = new TabPage("イベントトリガ") { Padding = new Padding(4) };
        _triggerPanel = new TriggerPanel { Dock = DockStyle.Fill };
        _triggerPanel.DefinitionsChanged += (_, _) =>
        {
            _triggerEngine.SetTriggers(_projectManager.Current.Triggers, preserveRuntimeState: true);
            _projectManager.MarkDirty();
        };
        triggersTab.Controls.Add(_triggerPanel);
        _tabs.TabPages.Add(soundsTab);
        _tabs.TabPages.Add(triggersTab);
        root.Controls.Add(_tabs, 0, 2);

        var statusStrip = new StatusStrip
        {
            SizingGrip = false,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(245, 247, 249),
            RenderMode = ToolStripRenderMode.System,
            Padding = new Padding(6, 2, 6, 2)
        };
        _statusLabel = new ToolStripStatusLabel("状態: 準備完了")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.ControlText,
            BorderSides = ToolStripStatusLabelBorderSides.Top,
            BorderStyle = Border3DStyle.Etched
        };
        statusStrip.Items.Add(_statusLabel);
        root.Controls.Add(statusStrip, 0, 3);
        Controls.Add(root);

        _shortcutDispatcher = new ShortcutDispatcher(
            new ShortcutManager(),
            () => _projectManager.Current.Sounds,
            () => _projectManager.Current.GroupShortcuts,
            IsEditingText,
            ExecuteShortcuts);
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
        Deactivate += (_, _) => _shortcutDispatcher.Reset();

        ApplyProjectSettings();
        RebuildSoundCards();
        RefreshRecentMenu();
    }

    internal void CloseWithoutSavePromptForSmokeTest()
    {
        _closingAfterSave = true;
        Close();
    }

    private MenuStrip BuildMenu(
        out ToolStripMenuItem recentMenu,
        out ToolStripMenuItem muteMenuItem)
    {
        var menu = new MenuStrip { Dock = DockStyle.Fill };
        var file = new ToolStripMenuItem("ファイル(&F)");
        var newItem = new ToolStripMenuItem("新規(&N)", null, async (_, _) => await NewProjectAsync());
        var openItem = new ToolStripMenuItem("開く(&O)…", null, async (_, _) => await OpenWithDialogAsync());
        var saveItem = new ToolStripMenuItem("保存(&S)", null, async (_, _) => await SaveProjectAsync(forceSaveAs: false));
        var saveAsItem = new ToolStripMenuItem("名前を付けて保存(&A)…", null, async (_, _) => await SaveProjectAsync(forceSaveAs: true));
        newItem.ShortcutKeys = Keys.Control | Keys.N;
        openItem.ShortcutKeys = Keys.Control | Keys.O;
        saveItem.ShortcutKeys = Keys.Control | Keys.S;
        saveAsItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.S;
        var addItem = new ToolStripMenuItem("音源を追加…", null, (_, _) => AddSoundsWithDialog());
        recentMenu = new ToolStripMenuItem("最近使ったプロジェクト");
        file.DropDownItems.AddRange([newItem, openItem, saveItem, saveAsItem, new ToolStripSeparator(), addItem, recentMenu]);

        var operations = new ToolStripMenuItem("操作(&O)");
        var stopAllItem = new ToolStripMenuItem("全停止(&R)", null, (_, _) => StopAll())
        {
            ShortcutKeyDisplayString = "Ctrl+R"
        };
        muteMenuItem = new ToolStripMenuItem("ミュート(&M)", null, (_, _) => ToggleMasterMute());
        var groupShortcuts = new ToolStripMenuItem("グループショートカット(&G)…", null, (_, _) => ShowGroupShortcuts());
        var soundOrder = new ToolStripMenuItem("音源の並び替え(&L)…", null, (_, _) => ShowSoundOrder());
        var diagnostics = new ToolStripMenuItem("動作診断(&D)…", null, async (_, _) => await RunSelfTestAsync());
        var timings = new ToolStripMenuItem("再生開始時間をログに記録") { CheckOnClick = true };
        timings.CheckedChanged += (_, _) =>
        {
            _audioEngine.PlaybackTimingEnabled = timings.Checked;
            SetStatus(timings.Checked
                ? "再生開始の内部処理時間を計測します。実際の出音遅延ではありません。"
                : "再生開始の計測を終了しました。", false);
        };
        var emergencyStop = new ToolStripMenuItem("緊急強制停止（フェードなし）", null, (_, _) => EmergencyStopAll());
        operations.DropDownItems.AddRange(
            [stopAllItem, muteMenuItem, new ToolStripSeparator(), soundOrder, groupShortcuts,
                new ToolStripSeparator(), diagnostics, timings, new ToolStripSeparator(), emergencyStop]);

        var settings = new ToolStripMenuItem("設定(&S)", null, (_, _) => ShowSettings());
        var help = new ToolStripMenuItem("ヘルプ(&H)");
        var usage = new ToolStripMenuItem("使い方(&H)…", null, (_, _) => ShowHelp());
        help.DropDownItems.Add(usage);
        menu.Items.AddRange([file, operations, settings, help]);
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
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        projectLabel = HeaderLabel("プロジェクト: 新しいプロジェクト", 11, FontStyle.Bold);
        deviceLabel = HeaderLabel("出力: Windows 既定", 10, FontStyle.Regular);
        clockModeLabel = HeaderLabel("現在時刻", 10, FontStyle.Bold);
        clockLabel = HeaderLabel("00:00:00", 20, FontStyle.Bold);
        clockLabel.TextAlign = ContentAlignment.MiddleCenter;
        clockLabel.AutoEllipsis = false;

        var addButton = HeaderButton("＋ 音源追加", Color.FromArgb(225, 235, 245));
        var stopAllButton = HeaderButton("■ 全停止", Color.FromArgb(220, 65, 65));
        _stopAllButton = stopAllButton;
        addButton.Tag = UiTheme.AccentButtonRole;
        stopAllButton.Tag = UiTheme.DangerButtonRole;
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
            TickStyle = TickStyle.None,
            Value = 100,
            Dock = DockStyle.Fill
        };
        masterVolume = masterVolumeControl;
        var masterVolumeValue = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100,
            DecimalPlaces = 0,
            Increment = 1,
            Value = 100,
            Dock = DockStyle.Fill,
            TextAlign = HorizontalAlignment.Right,
            Margin = new Padding(3, 5, 0, 5)
        };
        _masterVolumeValue = masterVolumeValue;
        var masterMuteControl = new CheckBox
        {
            Text = "ミュート",
            ForeColor = Color.White,
            Appearance = Appearance.Button,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = UiTheme.CreateFont(10F, FontStyle.Bold),
            BackColor = Color.FromArgb(58, 63, 68),
            UseVisualStyleBackColor = false,
            Margin = new Padding(8, 4, 8, 4)
        };
        masterMute = masterMuteControl;
        var masterPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        masterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        masterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        masterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        masterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
        var masterCaption = HeaderLabel("MASTER", 8.5F, FontStyle.Bold);
        masterCaption.AutoEllipsis = false;
        masterCaption.UseCompatibleTextRendering = false;
        masterPanel.Controls.Add(masterCaption, 0, 0);
        masterPanel.Controls.Add(masterVolumeControl, 1, 0);
        masterPanel.Controls.Add(masterVolumeValue, 2, 0);
        masterPanel.Controls.Add(HeaderLabel("%", 9F, FontStyle.Bold), 3, 0);

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

        var projectLabelControl = projectLabel;
        var deviceLabelControl = deviceLabel;
        var clockLabelControl = clockLabel;
        var clockModeLabelControl = clockModeLabel;
        var compactClock = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = Padding.Empty };
        compactClock.RowStyles.Add(new RowStyle(SizeType.Absolute, 22 * header.DeviceDpi / 96F));
        compactClock.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bool? compactLayout = null;
        var layoutQueued = false;
        void QueueHeaderLayout()
        {
            if (layoutQueued || !header.IsHandleCreated || header.IsDisposed) { return; }
            layoutQueued = true;
            header.BeginInvoke(() =>
            {
                layoutQueued = false;
                if (!header.IsDisposed && header.Parent is TableLayoutPanel parent)
                {
                    parent.PerformLayout();
                }
            });
        }
        void ArrangeHeader()
        {
            var compact = header.ClientSize.Width < (int)(1100 * header.DeviceDpi / 96F);
            var desiredHeight = (compact ? 236 : 104) * header.DeviceDpi / 96F;
            if (header.Parent is TableLayoutPanel parent && parent.RowStyles[1].Height != desiredHeight)
            {
                parent.RowStyles[1].Height = desiredHeight;
                QueueHeaderLayout();
            }
            if (compactLayout == compact)
            {
                return;
            }

            compactLayout = compact;
            header.SuspendLayout();
            header.Controls.Clear();
            header.ColumnStyles.Clear();
            header.RowStyles.Clear();
            header.ColumnCount = compact ? 3 : 6;
            header.RowCount = compact ? 5 : 2;
            if (compact)
            {
                header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                foreach (var height in new[] { 30, 40, 66, 40, 40 })
                {
                    header.RowStyles.Add(new RowStyle(SizeType.Absolute, height * header.DeviceDpi / 96F));
                }

                header.Controls.Add(projectLabelControl, 0, 0);
                header.SetColumnSpan(projectLabelControl, 2);
                header.Controls.Add(deviceLabelControl, 2, 0);
                header.Controls.Add(masterPanel, 0, 1);
                header.SetColumnSpan(masterPanel, 2);
                header.Controls.Add(masterMuteControl, 2, 1);
                compactClock.Controls.Add(clockModeLabelControl, 0, 0);
                compactClock.Controls.Add(clockLabelControl, 0, 1);
                header.Controls.Add(compactClock, 0, 2);
                header.Controls.Add(modeButton, 1, 2);
                header.Controls.Add(startButton, 0, 3);
                header.Controls.Add(resetButton, 1, 3);
                header.Controls.Add(addButton, 0, 4);
                header.SetColumnSpan(addButton, 2);
                header.Controls.Add(stopAllButton, 2, 2);
                header.SetRowSpan(stopAllButton, 3);
            }
            else
            {
                header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300 * header.DeviceDpi / 96F));
                foreach (var width in new[] { 190, 130, 120, 160 })
                {
                    header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width * header.DeviceDpi / 96F));
                }

                header.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                header.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                header.Controls.Add(projectLabelControl, 0, 0);
                header.SetColumnSpan(projectLabelControl, 1);
                header.Controls.Add(deviceLabelControl, 0, 1);
                header.Controls.Add(masterPanel, 1, 0);
                header.SetColumnSpan(masterPanel, 1);
                header.Controls.Add(masterMuteControl, 1, 1);
                header.Controls.Add(clockModeLabelControl, 2, 0);
                header.Controls.Add(clockLabelControl, 2, 1);
                header.Controls.Add(modeButton, 3, 0);
                header.Controls.Add(startButton, 3, 1);
                header.Controls.Add(addButton, 4, 0);
                header.SetColumnSpan(addButton, 1);
                header.Controls.Add(resetButton, 4, 1);
                header.Controls.Add(stopAllButton, 5, 0);
                header.SetRowSpan(stopAllButton, 2);
            }

            header.ResumeLayout(true);
            QueueHeaderLayout();
        }

        header.SizeChanged += (_, _) => ArrangeHeader();
        header.ParentChanged += (_, _) => ArrangeHeader();
        header.HandleCreated += (_, _) => QueueHeaderLayout();
        header.Disposed += (_, _) => compactClock.Dispose();
        header.Disposed += (_, _) => _headerToolTip.Dispose();
        header.Disposed += (_, _) => clockModeLabelControl.Dispose();

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
            _triggerEngine.ResetStopwatch();
            _stopwatchMode = true;
            UpdateClockDisplay();
        };
        masterVolumeControl.ValueChanged += (_, _) =>
        {
            if (masterVolumeValue.Value != masterVolumeControl.Value)
            {
                masterVolumeValue.Value = masterVolumeControl.Value;
            }

            if (_applyingProjectSettings)
            {
                return;
            }

            var volume = masterVolumeControl.Value / 100.0f;
            _audioEngine.MasterVolume = volume;
            _projectManager.Current.Settings.MasterVolume = volume;
            _projectManager.MarkDirty();
        };
        masterVolumeValue.ValueChanged += (_, _) =>
        {
            var value = decimal.ToInt32(masterVolumeValue.Value);
            if (masterVolumeControl.Value != value)
            {
                masterVolumeControl.Value = value;
            }
        };
        masterMuteControl.CheckedChanged += (_, _) =>
        {
            UpdateMuteAppearance(masterMuteControl);
            _muteMenuItem.Checked = masterMuteControl.Checked;
            if (_applyingProjectSettings)
            {
                return;
            }

            _audioEngine.MasterMute = masterMuteControl.Checked;
            _projectManager.Current.Settings.MasterMute = masterMuteControl.Checked;
            _projectManager.MarkDirty();
        };
        UpdateMuteAppearance(masterMuteControl);
        return header;
    }

    private static Label HeaderLabel(string text, float size, FontStyle style) => new()
    {
        Text = text,
        ForeColor = Color.White,
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = UiTheme.CreateFont(size, style)
    };

    private static Button HeaderButton(string text, Color color) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        BackColor = color,
        UseVisualStyleBackColor = false,
        Margin = new Padding(4),
        Font = UiTheme.CreateFont(10F, FontStyle.Bold)
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
        if (_projectOperationInProgress)
        {
            SetStatus("別のプロジェクト操作が完了するまで待ってください。", true);
            return;
        }

        if (!await EnsureCanReplaceProjectAsync())
        {
            return;
        }

        try
        {
            SetProjectOperationBusy(true);
            var opened = await _projectManager.OpenAsync(path, AuthorizeUntrustedProject);
            if (!opened)
            {
                SetStatus("プロジェクトの読み込みをキャンセルしました。", false);
                return;
            }

            _audioEngine.StopAll();
            _recentProjects.Add(path);
            _logger.Info($"プロジェクトを開きました: {path}");
            ApplyProjectSettings();
            await RebuildSoundCardsAsync();
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
        finally
        {
            SetProjectOperationBusy(false);
        }
    }

    private async Task<bool> SaveProjectAsync(bool forceSaveAs)
    {
        if (_projectOperationInProgress)
        {
            SetStatus("別のプロジェクト操作が完了するまで待ってください。", true);
            return false;
        }

        try
        {
            var savingProject = _projectManager.Current;
            _ = _masterVolumeValue.Value;
            SetProjectOperationBusy(true);
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

            if (!ReferenceEquals(savingProject, _projectManager.Current) || _projectManager.IsDirty)
            {
                SetStatus("保存中の変更が残っています。もう一度保存してください。終了・切替は中止しました。", true);
                return false;
            }

            if (_projectManager.CurrentPath is { } savedPath)
            {
                _recentProjects.Add(savedPath);
            }
            _logger.Info($"プロジェクトを保存しました: {_projectManager.CurrentPath}");
            RefreshRecentMenu();
            if (_projectManager.LastSaveResult is { BackupWarning: { } warning } result)
            {
                _logger.Warning($"{warning} 復旧用バックアップ: {result.RecoveryBackupPath}");
                SetStatus($"保存しました（バックアップ整理に失敗）。復旧用: {result.RecoveryBackupPath}", true);
            }
            else
            {
                SetStatus("保存しました。", false);
            }
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error("プロジェクトを保存できません。", exception);
            MessageBox.Show(this, exception.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            SetProjectOperationBusy(false);
        }
    }

    private void AddSoundsWithDialog()
    {
        if (_projectOperationInProgress)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = AudioFileFormats.OpenFileFilter,
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
        if (_projectOperationInProgress)
        {
            return;
        }

        var added = new List<SoundDefinition>();
        foreach (var path in paths)
        {
            if (!IsSupportedAudioFile(path))
            {
                SetStatus($"未対応形式をスキップしました: {Path.GetFileName(path)}", true);
                continue;
            }

            try
            {
                added.Add(_projectManager.AddSound(path));
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                _logger.Warning($"音源を追加できません: {path} ({exception.Message})");
                SetStatus($"音源を追加できません: {exception.Message}", true);
            }
        }

        if (added.Count > 0)
        {
            _soundList.SuspendLayout();
            try
            {
                foreach (var sound in added)
                {
                    AddSoundCard(sound);
                }

                ResizeSoundCards();
            }
            finally
            {
                _soundList.ResumeLayout();
            }

            RefreshTriggerData(resetEngine: false);
            SetStatus($"音源を{added.Count}件追加しました。", false);
        }
    }

    private void RebuildSoundCards()
    {
        _soundList.SuspendLayout();
        try
        {
            foreach (var existing in _soundList.Controls.Cast<Control>().ToArray())
            {
                existing.Dispose();
            }

            _soundList.Controls.Clear();
            _soundCards.Clear();
            foreach (var sound in _projectManager.Current.Sounds)
            {
                AddSoundCard(sound);
            }

            ResizeSoundCards();
            RefreshTriggerData();
        }
        finally
        {
            _soundList.ResumeLayout();
        }
    }

    private async Task RebuildSoundCardsAsync()
    {
        _soundList.SuspendLayout();
        try
        {
            foreach (var existing in _soundList.Controls.Cast<Control>().ToArray())
            {
                existing.Dispose();
            }

            _soundList.Controls.Clear();
            _soundCards.Clear();
        }
        finally
        {
            _soundList.ResumeLayout();
        }

        var sounds = _projectManager.Current.Sounds.ToArray();
        const int batchSize = 25;
        for (var offset = 0; offset < sounds.Length; offset += batchSize)
        {
            _soundList.SuspendLayout();
            try
            {
                foreach (var sound in sounds.Skip(offset).Take(batchSize))
                {
                    AddSoundCard(sound);
                }
            }
            finally
            {
                _soundList.ResumeLayout();
            }

            await Task.Yield();
        }

        ResizeSoundCards();
        RefreshTriggerData();
    }

    private void AddSoundCard(SoundDefinition sound)
    {
        var card = new SoundCard(sound, () => _projectManager.ResolveSoundPath(sound));
        card.ApplyDarkMode(_projectManager.Current.Settings.DarkMode);
        card.PlayRequested += async (_, _) => await PlaySoundAsync(sound.Id);
        card.StopRequested += (_, _) => _audioEngine.Stop(sound.Id);
        card.PauseRequested += (_, _) => _audioEngine.Pause(sound.Id);
        card.ResumeRequested += (_, _) => _audioEngine.Resume(sound.Id);
        card.FileBrowseRequested += (_, _) => BrowseReplacementFile(sound, card);
        card.DeleteRequested += (_, _) => DeleteSound(sound, card);
        card.InvalidKeyRejected += (_, _) =>
            SetStatus("使用できないキー名だったため、割り当てを解除しました。A、2、F1などを入力してください。", true);
        card.GroupChangeCommitted += (_, eventArgs) => OnSoundGroupChangeCommitted(eventArgs);
        card.VolumeFadeRequested += (_, eventArgs) =>
        {
            _audioEngine.FadeVolume(sound.Id, eventArgs.TargetVolume, eventArgs.Duration);
            _projectManager.MarkDirty();
        };
        var displayName = sound.Name;
        card.DefinitionChanged += (_, _) =>
        {
            _audioEngine.ApplyLiveSettings(sound);
            if (!string.Equals(displayName, sound.Name, StringComparison.Ordinal))
            {
                displayName = sound.Name;
                _triggerPanel.RefreshSoundNames();
            }
            _projectManager.MarkDirty();
        };
        card.UpdateState(_audioEngine.GetState(sound.Id));
        _soundCards[sound.Id] = card;
        _soundList.Controls.Add(card);
    }

    private void ShowSoundOrder()
    {
        if (_projectOperationInProgress)
        {
            return;
        }

        var project = _projectManager.Current;
        if (project.Sounds.Count < 2)
        {
            SetStatus("並び替えるには音源を2件以上追加してください。", false);
            return;
        }

        using var dialog = new SoundOrderDialog(project.Sounds);
        UiTheme.Apply(dialog, project.Settings.DarkMode);
        if (dialog.ShowDialog(this) != DialogResult.OK
            || !ReferenceEquals(project, _projectManager.Current)
            || !ApplySoundOrder(project.Sounds, dialog.OrderedSoundIds))
        {
            return;
        }

        _soundList.SuspendLayout();
        try
        {
            for (var index = 0; index < project.Sounds.Count; index++)
            {
                _soundList.Controls.SetChildIndex(_soundCards[project.Sounds[index].Id], index);
            }
        }
        finally
        {
            _soundList.ResumeLayout(true);
        }

        _projectManager.MarkDirty();
        SetStatus("音源を並べ替えました。", false);
    }

    internal static bool ApplySoundOrder(List<SoundDefinition> sounds, IReadOnlyList<Guid> orderedIds)
    {
        if (orderedIds.Count != sounds.Count || orderedIds.Distinct().Count() != sounds.Count)
        {
            return false;
        }

        var byId = sounds.ToDictionary(sound => sound.Id);
        if (orderedIds.Any(id => !byId.ContainsKey(id)) || sounds.Select(sound => sound.Id).SequenceEqual(orderedIds))
        {
            return false;
        }

        var ordered = orderedIds.Select(id => byId[id]).ToArray();
        sounds.Clear();
        sounds.AddRange(ordered);
        return true;
    }

    internal static bool ReorderSoundDefinitions(
        IList<SoundDefinition> sounds,
        Guid sourceSoundId,
        Guid targetSoundId,
        bool insertAfter,
        out int newIndex)
    {
        newIndex = -1;
        var sourceIndex = sounds
            .Select((sound, index) => (sound, index))
            .FirstOrDefault(item => item.sound.Id == sourceSoundId)
            .index;
        var sourceExists = sourceIndex >= 0 && sourceIndex < sounds.Count
            && sounds[sourceIndex].Id == sourceSoundId;
        if (!sourceExists || sourceSoundId == targetSoundId)
        {
            return false;
        }

        var source = sounds[sourceIndex];
        sounds.RemoveAt(sourceIndex);
        var targetIndex = sounds
            .Select((sound, index) => (sound, index))
            .FirstOrDefault(item => item.sound.Id == targetSoundId)
            .index;
        var targetExists = targetIndex >= 0 && targetIndex < sounds.Count
            && sounds[targetIndex].Id == targetSoundId;
        if (!targetExists)
        {
            sounds.Insert(sourceIndex, source);
            return false;
        }

        newIndex = targetIndex + (insertAfter ? 1 : 0);
        sounds.Insert(newIndex, source);
        return newIndex != sourceIndex;
    }

    private void RefreshTriggerData(bool resetEngine = true)
    {
        _triggerPanel.SetData(_projectManager.Current.Triggers, _projectManager.Current.Sounds);
        _triggerEngine.SetTriggers(
            _projectManager.Current.Triggers,
            preserveRuntimeState: !resetEngine);
    }

    private void DeleteSound(SoundDefinition sound, SoundCard card)
    {
        var relatedSettings = CountSoundRemovalReferences(_projectManager.Current, sound);
        var relatedMessage = relatedSettings == 0
            ? string.Empty
            : $"\n\nこの音源を参照するトリガ・グループ設定 {relatedSettings} 件も削除します。";
        var confirmation = MessageBox.Show(
            this,
            $"「{sound.Name}」をプロジェクトから削除しますか？\n音声ファイル自体は削除しません。{relatedMessage}",
            "音源の削除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        _audioEngine.StopImmediately(sound.Id);
        var result = RemoveSoundDefinition(_projectManager.Current, sound.Id);
        if (!result.Removed)
        {
            return;
        }

        _soundCards.Remove(sound.Id);
        _soundList.Controls.Remove(card);
        card.Dispose();
        RefreshTriggerData(resetEngine: false);
        ResizeSoundCards();
        _projectManager.MarkDirty();
        var suffix = result.RelatedSettingsRemoved == 0
            ? string.Empty
            : $"（関連設定 {result.RelatedSettingsRemoved} 件も削除）";
        SetStatus($"音源を削除しました: {sound.Name}{suffix}", false);
    }

    internal static int CountSoundRemovalReferences(ProjectModel model, SoundDefinition sound)
    {
        var isLastGroupMember = !string.IsNullOrWhiteSpace(sound.Group)
            && !model.Sounds.Any(candidate => candidate.Id != sound.Id
                && string.Equals(candidate.Group, sound.Group, StringComparison.OrdinalIgnoreCase));
        var affectedTriggers = model.Triggers.Count(trigger =>
            trigger.TargetSoundId == sound.Id
            || trigger.Parameters.SourceSoundId == sound.Id
            || (isLastGroupMember
                && string.Equals(trigger.TargetGroup, sound.Group, StringComparison.OrdinalIgnoreCase)));
        var affectedShortcuts = isLastGroupMember
            ? model.GroupShortcuts.Count(shortcut =>
                string.Equals(shortcut.Group, sound.Group, StringComparison.OrdinalIgnoreCase))
            : 0;
        return affectedTriggers + affectedShortcuts;
    }

    internal static SoundRemovalResult RemoveSoundDefinition(ProjectModel model, Guid soundId)
    {
        var sound = model.Sounds.FirstOrDefault(candidate => candidate.Id == soundId);
        if (sound is null)
        {
            return new SoundRemovalResult(false, 0);
        }

        var removedTriggerCount = model.Triggers.RemoveAll(trigger =>
            trigger.TargetSoundId == soundId || trigger.Parameters.SourceSoundId == soundId);
        var removedGroupSettings = 0;
        var isLastGroupMember = !string.IsNullOrWhiteSpace(sound.Group)
            && !model.Sounds.Any(candidate => candidate.Id != soundId
                && string.Equals(candidate.Group, sound.Group, StringComparison.OrdinalIgnoreCase));
        if (isLastGroupMember)
        {
            removedGroupSettings += model.GroupShortcuts.RemoveAll(shortcut =>
                string.Equals(shortcut.Group, sound.Group, StringComparison.OrdinalIgnoreCase));
            removedGroupSettings += model.Triggers.RemoveAll(trigger =>
                string.Equals(trigger.TargetGroup, sound.Group, StringComparison.OrdinalIgnoreCase));
        }

        model.Sounds.Remove(sound);
        return new SoundRemovalResult(true, removedTriggerCount + removedGroupSettings);
    }

    private void ResizeSoundCards()
    {
        const int minimumCardWidth = 340;
        const int gap = 12;
        var availableWidth = Math.Max(minimumCardWidth, _soundList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 20);
        var requestedColumns = Math.Clamp(_projectManager.Current.Settings.SoundListColumns, 1, 8);
        var columnsThatFit = Math.Max(1, availableWidth / (minimumCardWidth + gap));
        var columns = Math.Min(requestedColumns, columnsThatFit);
        var width = Math.Max(minimumCardWidth, (availableWidth - (columns * gap)) / columns);
        foreach (var card in _soundCards.Values)
        {
            card.Width = width;
        }
    }

    private void BrowseReplacementFile(SoundDefinition sound, SoundCard card)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = AudioFileFormats.OpenFileFilter,
            CheckFileExists = true,
            Multiselect = false,
            Title = $"{sound.Name} の音声ファイルを差し替え"
        };
        try
        {
            var currentPath = _projectManager.ResolveSoundPath(sound);
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            dialog.FileName = Path.GetFileName(currentPath);
        }
        catch
        {
            // 壊れた旧パスでも参照ダイアログ自体は利用できるようにする。
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _audioEngine.Stop(sound.Id);
            _projectManager.ReplaceSoundFile(sound, dialog.FileName);
            card.UpdateFilePath(sound.File);
            SetStatus($"音声ファイルを差し替えました: {sound.Name}", false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            _logger.Warning($"音源を差し替えられません: {dialog.FileName} ({exception.Message})");
            MessageBox.Show(this, exception.Message, "音源差し替えエラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    private void StopAll(bool useConfiguredFade = true)
    {
        var useFadeOut = useConfiguredFade && _projectManager.Current.Settings.FadeOutOnStopAll;
        _audioEngine.StopAll(useFadeOut);
        SetStatus(
            useFadeOut
                ? "全停止を実行しました。Fade Out設定のある音源はフェード後に停止します。"
                : "すべての音源を即時停止しました。",
            false);
    }

    private void ToggleMasterMute()
    {
        _masterMute.Checked = !_masterMute.Checked;
        SetStatus(_masterMute.Checked ? "ミュートしました。" : "ミュートを解除しました。", false);
    }

    private void EmergencyStopAll()
    {
        Interlocked.Increment(ref _playbackGeneration);
        // Keep held-key state so an auto-repeat does not count as a new press after the stop.
        _audioEngine.EmergencyStopAll();
        SetStatus("緊急強制停止：すべての音源と保留中の再生を即時停止しました。", false);
    }

    private void ExecuteShortcuts(IReadOnlyList<ShortcutCommand> commands)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            var commandSnapshot = commands.ToArray();
            var generation = Interlocked.Read(ref _playbackGeneration);
            TryBeginInvoke(() =>
            {
                if (generation == Interlocked.Read(ref _playbackGeneration))
                {
                    ExecuteShortcuts(commandSnapshot);
                }
            });
            return;
        }

        var playIds = commands
            .Where(command => command.Type == ShortcutCommandType.Play && command.SoundId is not null)
            .Select(command => command.SoundId!.Value)
            .Distinct()
            .ToArray();
        if (playIds.Length > 1)
        {
            PlayShortcutBatch(playIds);
            return;
        }

        foreach (var command in commands)
        {
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
    }

    private void PlayShortcutBatch(IReadOnlyCollection<Guid> soundIds)
    {
        foreach (var soundId in soundIds)
        {
            _ = PlaySoundAsync(soundId);
        }
    }

    private void ShowSettings()
    {
        var devices = _deviceManager.GetOutputDevices();
        using var dialog = new SettingsDialog(_projectManager.Current.Settings, devices);
        dialog.SetProjectMetadata(_projectManager.Current.Project);
        UiTheme.Apply(dialog, _projectManager.Current.Settings.DarkMode);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var settings = _projectManager.Current.Settings;
        var selectedDevice = devices.FirstOrDefault(
            device => device.DeviceNumber == dialog.SelectedDeviceNumber)
            ?? AudioDeviceInfo.Default;
        var project = _projectManager.Current.Project;
        var projectName = string.IsNullOrWhiteSpace(dialog.ProjectName)
            ? "名称未設定"
            : dialog.ProjectName;
        var changed = HasProjectSettingsChanges(
            settings,
            project,
            selectedDevice.PersistentId,
            dialog.BackgroundInput,
            dialog.SilentLoop,
            dialog.FadeOutOnStop,
            dialog.FadeOutOnStopAll,
            dialog.DarkMode,
            dialog.SoundListColumns,
            projectName,
            dialog.ThemeColor);
        settings.OutputDevice = selectedDevice.PersistentId;
        settings.BackgroundInput = dialog.BackgroundInput;
        settings.SilentLoop = dialog.SilentLoop;
        settings.FadeOutOnStop = dialog.FadeOutOnStop;
        settings.FadeOutOnStopAll = dialog.FadeOutOnStopAll;
        settings.DarkMode = dialog.DarkMode;
        settings.SoundListColumns = dialog.SoundListColumns;
        project.Name = projectName;
        project.ThemeColor = dialog.ThemeColor;
        if (changed)
        {
            _projectManager.MarkDirty();
        }

        ApplyProjectSettings();
        ResizeSoundCards();
        SetStatus(changed ? "設定を更新しました。" : "設定に変更はありません。", false);
    }

    internal static bool HasProjectSettingsChanges(
        ProjectSettings settings,
        ProjectMetadata project,
        string outputDevice,
        bool backgroundInput,
        bool silentLoop,
        bool fadeOutOnStop,
        bool fadeOutOnStopAll,
        bool darkMode,
        int soundListColumns,
        string projectName,
        string themeColor) =>
        !string.Equals(settings.OutputDevice, outputDevice, StringComparison.Ordinal)
        || settings.BackgroundInput != backgroundInput
        || settings.SilentLoop != silentLoop
        || settings.FadeOutOnStop != fadeOutOnStop
        || settings.FadeOutOnStopAll != fadeOutOnStopAll
        || settings.DarkMode != darkMode
        || settings.SoundListColumns != soundListColumns
        || !string.Equals(project.Name, projectName, StringComparison.Ordinal)
        || !string.Equals(project.ThemeColor, themeColor, StringComparison.Ordinal);

    private void ShowGroupShortcuts()
    {
        var groups = _projectManager.Current.Sounds
            .Select(sound => sound.Group)
            .Where(group => !string.IsNullOrWhiteSpace(group));
        using var dialog = new GroupShortcutDialog(_projectManager.Current.GroupShortcuts, groups);
        UiTheme.Apply(dialog, _projectManager.Current.Settings.DarkMode);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var definitions = dialog.Definitions.ToList();
        if (GroupShortcutsEqual(_projectManager.Current.GroupShortcuts, definitions))
        {
            SetStatus("グループショートカットに変更はありません。", false);
            return;
        }

        _projectManager.Current.GroupShortcuts = definitions;
        _projectManager.MarkDirty();
        SetStatus("グループショートカットを更新しました。", false);
    }

    internal static bool GroupShortcutsEqual(
        IReadOnlyList<GroupShortcutDefinition> first,
        IReadOnlyList<GroupShortcutDefinition> second) => first.Count == second.Count
        && first.Zip(second).All(pair =>
            string.Equals(pair.First.Group, pair.Second.Group, StringComparison.Ordinal)
            && string.Equals(pair.First.Key, pair.Second.Key, StringComparison.Ordinal));

    private void OnSoundGroupChangeCommitted(GroupChangedEventArgs eventArgs)
    {
        var previousGroup = eventArgs.PreviousGroup.Trim();
        var currentGroup = eventArgs.CurrentGroup.Trim();
        if (previousGroup.Length == 0
            || string.Equals(previousGroup, currentGroup, StringComparison.Ordinal)
            || _projectManager.Current.Sounds.Any(sound =>
                string.Equals(sound.Group, previousGroup, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var referenceCount = CountGroupReferences(_projectManager.Current, previousGroup);
        if (referenceCount == 0)
        {
            return;
        }

        if (currentGroup.Length == 0)
        {
            SetStatus($"グループ「{previousGroup}」を参照する設定が{referenceCount}件残っています。", true);
            return;
        }

        var updated = RetargetGroupReferences(_projectManager.Current, previousGroup, currentGroup);
        RefreshTriggerData(resetEngine: false);
        _projectManager.MarkDirty();
        SetStatus($"グループ名の変更に合わせて関連設定を{updated}件更新しました。", false);
    }

    internal static int RetargetGroupReferences(ProjectModel model, string previousGroup, string currentGroup)
    {
        var updated = 0;
        foreach (var shortcut in model.GroupShortcuts.Where(shortcut =>
                     string.Equals(shortcut.Group, previousGroup, StringComparison.OrdinalIgnoreCase)))
        {
            shortcut.Group = currentGroup;
            updated++;
        }

        foreach (var trigger in model.Triggers.Where(trigger =>
                     string.Equals(trigger.TargetGroup, previousGroup, StringComparison.OrdinalIgnoreCase)))
        {
            trigger.TargetGroup = currentGroup;
            updated++;
        }

        var uniqueShortcuts = new List<GroupShortcutDefinition>();
        var seenShortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var shortcut in model.GroupShortcuts)
        {
            if (seenShortcuts.Add($"{shortcut.Group}\0{shortcut.Key}"))
            {
                uniqueShortcuts.Add(shortcut);
            }
        }

        model.GroupShortcuts = uniqueShortcuts;
        return updated;
    }

    private static int CountGroupReferences(ProjectModel model, string group) =>
        model.GroupShortcuts.Count(shortcut =>
            string.Equals(shortcut.Group, group, StringComparison.OrdinalIgnoreCase))
        + model.Triggers.Count(trigger =>
            string.Equals(trigger.TargetGroup, group, StringComparison.OrdinalIgnoreCase));

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

        StopAll(useConfiguredFade: false);
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
        _audioEngine.FadeOutOnStopEnabled = settings.FadeOutOnStop;

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
            try
            {
                _globalHook.Disable();
            }
            catch (Exception cleanupException)
            {
                _logger.Error("バックグラウンド入力の後始末に失敗しました。", cleanupException);
            }

            _logger.Error("バックグラウンド入力を有効にできません。", exception);
            SetStatus("バックグラウンド入力を開始できません。", true);
        }

        Color themeColor;
        try
        {
            themeColor = ColorTranslator.FromHtml(_projectManager.Current.Project.ThemeColor);
        }
        catch
        {
            themeColor = Color.FromArgb(36, 40, 44);
        }

        ApplyDisplayTheme(settings.DarkMode);
        ApplyHeaderContrast(themeColor);

        UpdateProjectHeader();
    }

    private void UpdateClockAndTriggers()
    {
        UpdateClockDisplay();
        CheckSelectedDeviceAvailability();
        _triggerEngine.Tick(DateTimeOffset.Now, _stopwatch.Elapsed);
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
            _clockLabel.Text = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
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
        var targetIds = (string.IsNullOrWhiteSpace(trigger.TargetGroup)
            ? [trigger.TargetSoundId]
            : _projectManager.Current.Sounds
                .Where(sound => string.Equals(sound.Group, trigger.TargetGroup, StringComparison.OrdinalIgnoreCase))
                .Select(sound => sound.Id)
                .ToArray()).Where(id => id != Guid.Empty).ToArray();
        if (targetIds.Length == 0)
        {
            SetStatus($"イベントの対象が見つかりません: {trigger.Memo}", true);
            return;
        }

        foreach (var targetId in targetIds)
        {
            switch (trigger.Action)
            {
                case TriggerAction.Play:
                    _ = PlaySoundAsync(targetId);
                    break;
                case TriggerAction.Stop:
                    _audioEngine.Stop(targetId);
                    break;
                case TriggerAction.Pause:
                    _audioEngine.Pause(targetId);
                    break;
            }
        }

        SetStatus($"イベントを実行しました: {trigger.Memo}", false);
    }

    private void OnSoundStateChanged(object? sender, SoundStateChangedEventArgs eventArgs)
    {
        _triggerEngine.SetSoundPaused(eventArgs.SoundId, eventArgs.State == PlaybackState.Paused);
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            TryBeginInvoke(() => UpdateSoundStateDisplay(eventArgs));
            return;
        }

        UpdateSoundStateDisplay(eventArgs);
    }

    private void UpdateSoundStateDisplay(SoundStateChangedEventArgs eventArgs)
    {
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
        var path = _projectManager.CurrentPath;
        var fileName = path is null ? "未保存" : Path.GetFileName(path);
        Text = $"効果音ポン出しくんEX — {_projectManager.Current.Project.Name} [{fileName}]{dirty}";
        _headerToolTip.SetToolTip(_projectLabel, path ?? "まだ保存されていないプロジェクトです。");
        _stopAllButton.Text = _projectManager.Current.Settings.FadeOutOnStopAll
            ? "■ 全停止\n(フェード)" : "■ 全停止\n(即時)";
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
        if (_projectOperationInProgress)
        {
            SetStatus("プロジェクト操作が完了するまで待ってください。", true);
            return false;
        }

        _ = _masterVolumeValue.Value;
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

    private bool AuthorizeUntrustedProject(ProjectModel model, string projectPath)
    {
        var externalPathCount = model.Sounds.Count(sound =>
            !string.IsNullOrWhiteSpace(sound.File)
            && ProjectPath.Classify(projectPath, sound.File) == ProjectPathKind.ExternalLocal);
        var enabledTriggerCount = model.Triggers.Count(trigger => trigger.Enabled);
        if (externalPathCount == 0 && !model.Settings.BackgroundInput && enabledTriggerCount == 0)
        {
            return true;
        }

        var risks = new List<string>();
        if (externalPathCount > 0)
        {
            risks.Add($"・プロジェクトフォルダ外のローカル音源: {externalPathCount}件");
        }

        if (model.Settings.BackgroundInput)
        {
            risks.Add("・バックグラウンド入力: 有効");
        }

        if (enabledTriggerCount > 0)
        {
            risks.Add($"・有効なイベントトリガ: {enabledTriggerCount}件");
        }

        var message = "このプロジェクトは、外部ファイルまたは自動操作を使用します。\n"
            + "信頼できる作成元の場合だけ開いてください。\n\n"
            + string.Join("\n", risks)
            + "\n\n開きますか？";
        return MessageBox.Show(
            this,
            message,
            "プロジェクトの安全確認",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void SetProjectOperationBusy(bool busy)
    {
        _projectOperationInProgress = busy;
        if (MainMenuStrip is not null)
        {
            MainMenuStrip.Enabled = !busy;
        }

        _tabs.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_projectOperationInProgress)
        {
            eventArgs.Cancel = true;
            return;
        }

        _ = _masterVolumeValue.Value;
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
        _headerToolTip.Dispose();
        Application.RemoveMessageFilter(_messageFilter);
        _globalHook.Dispose();
        _audioEngine.StopAll();
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs eventArgs) => CleanupInput();

    private void OnProjectChanged(object? sender, EventArgs eventArgs)
    {
        _shortcutDispatcher.RefreshBindings();
        UpdateProjectHeader();
    }

    private void OnPlaybackStarted(object? sender, SoundPlaybackEventArgs eventArgs) =>
        _triggerEngine.OnSoundStarted(eventArgs.SoundId);

    private void OnPlaybackEnded(object? sender, SoundPlaybackEventArgs eventArgs) =>
        _triggerEngine.OnSoundEnded(
            eventArgs.SoundId,
            eventArgs.EndReason == PlaybackEndReason.Natural);

    private void OnGlobalKeyChanged(object? sender, GlobalKeyEventArgs eventArgs)
    {
        var generation = Interlocked.Read(ref _playbackGeneration);
        TryBeginInvoke(() =>
        {
            if (generation == Interlocked.Read(ref _playbackGeneration)
                && _projectManager.Current.Settings.BackgroundInput)
            {
                _shortcutDispatcher.OnKeyChanged(eventArgs.KeyData, eventArgs.IsDown);
            }
        });
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
        if (_projectOperationInProgress)
        {
            eventArgs.Effect = DragDropEffects.None;
            return;
        }

        if (eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            eventArgs.Effect = DragDropEffects.Copy;
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs eventArgs)
    {
        if (_projectOperationInProgress)
        {
            return;
        }

        if (eventArgs.Data?.GetData(DataFormats.FileDrop) is string[] paths)
        {
            AddSoundFiles(paths);
        }
    }


    private bool IsEditingText()
    {
        if (_projectOperationInProgress || !Enabled || OwnedForms.Any(form => form.Visible)
            || (Form.ActiveForm is { } active && active != this))
        {
            return true;
        }

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

    private void EndTextEditing()
    {
        if (!IsEditingText())
        {
            return;
        }

        var focusedCard = _soundCards.Values.FirstOrDefault(card => card.ContainsFocus);
        if (focusedCard is not null)
        {
            focusedCard.EndEditing();
        }
        else
        {
            ActiveControl = null;
        }
    }

    private void ShowHelp()
    {
        using var dialog = new HelpDialog();
        UiTheme.Apply(dialog, _projectManager.Current.Settings.DarkMode);
        dialog.ShowDialog(this);
    }

    private void ApplyDisplayTheme(bool darkMode)
    {
        _darkMode = darkMode;
        UiTheme.ConfigureApplicationColors(darkMode);
        UiTheme.Apply(this, darkMode);
        _triggerPanel.ApplyDarkMode(darkMode);
        foreach (var card in _soundCards.Values)
        {
            card.ApplyDarkMode(darkMode);
        }

        UpdateMuteAppearance(_masterMute);
        _tabs.Invalidate();
        var statusText = _statusLabel.Text ?? string.Empty;
        SetStatus(statusText.StartsWith("状態: ", StringComparison.Ordinal)
            ? statusText[4..]
            : statusText, _statusIsError);
    }

    private static void UpdateMuteAppearance(CheckBox mute)
    {
        mute.Text = mute.Checked ? "ミュート中" : "ミュート";
        mute.BackColor = mute.Checked ? Color.FromArgb(210, 62, 62) : Color.FromArgb(58, 63, 68);
        mute.ForeColor = Color.White;
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
        _statusIsError = error;
        _statusLabel.Text = $"状態: {message}";
        _statusLabel.ForeColor = error
            ? _darkMode ? Color.LightCoral : Color.DarkRed
            : _darkMode ? Color.FromArgb(238, 240, 242) : SystemColors.ControlText;
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
        var descendants = Descendants(header).ToArray();
        foreach (var container in descendants.Where(control =>
                     control is Panel or TableLayoutPanel or FlowLayoutPanel))
        {
            container.BackColor = themeColor;
        }

        foreach (var trackBar in descendants.OfType<TrackBar>())
        {
            trackBar.BackColor = themeColor;
        }

        foreach (var label in descendants.OfType<Label>())
        {
            label.ForeColor = foreground;
        }

        _masterMute.ForeColor = _masterMute.Checked ? Color.White : foreground;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool IsSupportedAudioFile(string path)
    {
        return AudioFileFormats.IsSupported(path);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "show" : sanitized;
    }
}

internal readonly record struct SoundRemovalResult(bool Removed, int RelatedSettingsRemoved);
