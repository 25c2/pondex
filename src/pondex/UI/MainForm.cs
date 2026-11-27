using System.Diagnostics;
using System.Globalization;
using pondex.Audio;
using pondex.Diagnostics;
using pondex.Input;
using pondex.Project;
using pondex.Trigger;
using pondex.Utility;

namespace pondex.UI;

public sealed class MainForm : Form
{
    private readonly ProjectManager _projectManager;
    private readonly IAudioEngine _audioEngine;
    private readonly AudioDeviceManager _deviceManager;
    private readonly TriggerEngine _triggerEngine;
    private readonly IAppLogger _logger;
    private readonly string _logDirectory;
    private readonly RecentProjectStore _recentProjects;
    private readonly UiPreferences? _uiPreferences;
    private int? _requestedHeaderHeight;
    private int _minimumHeaderHeight;
    private int? _dragHeaderHeight;
    private HeaderResizeGrip _headerGrip = null!;
    private readonly Stopwatch _stopwatch = new();
    private long _shortcutBindingsVersion = -1;
    private readonly Dictionary<Guid, SoundCard> _soundCards = [];
    private readonly GlobalKeyboardHook _globalHook;
    private readonly ShortcutDispatcher _shortcutDispatcher;
    private readonly ShortcutDispatcher _backgroundShortcutDispatcher;
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
    private ToolStripMenuItem _missingAudioMenuItem = null!;
    private readonly TrackBar _masterVolume;
    private readonly CheckBox _masterMute;
    private NumericUpDown _masterVolumeValue = null!;
    private ToolStripButton _resumeTriggersButton = null!;
    private Button _stopAllButton = null!;
    private Button _stopwatchToggleButton = null!;
    private readonly ToolTip _headerToolTip = new();
    private bool _stopwatchMode;
    private bool _closingAfterSave;
    private bool _applyingProjectSettings;
    private bool _cleanupCompleted;
    private bool _darkMode;
    private bool _statusIsError;
    private bool _loggingFailureShown;
    private bool _projectOperationInProgress;
    private bool _savingProject;
    private readonly List<ToolStripItem> _saveCommands = [];
    private DateTimeOffset _nextDeviceAvailabilityCheck = DateTimeOffset.MinValue;
    private long _playbackGeneration;
    private bool _outputUnavailable;
    private bool _outputNeedsReselection;
    private readonly OutputDevicePreferences? _outputPreferences;
    private OutputDevicePreference _outputPreference;
    private bool _editingLocked;
    private readonly List<ToolStripItem> _editingCommands = [];
    private ToolStripMenuItem _editLockMenuItem = null!;
    private ToolStripDropDownButton _editLockButton = null!;
    private ToolStripMenuItem _enableEditLockItem = null!;
    private ToolStripMenuItem _disableEditLockItem = null!;
    private Button _addSoundButton = null!;
    private AudioOutputBinding? _outputBinding;
    private AudioDeviceSnapshot? _lastOutputSnapshot;
    private bool _manualEmergencyStopping;
    private Task _manualEmergencyStopTask = Task.CompletedTask;
    private Task _outputConfigurationTask = Task.CompletedTask;
    private Task _deviceCheckTask = Task.CompletedTask;
    private long _outputConfigurationVersion;
    private readonly Dictionary<Guid, long> _uiPlayVersions = [];
    private readonly Dictionary<Guid, PlaybackState> _pendingPlayback = [];
    private bool _settingsDialogOpening;
    private CancellationTokenSource? _diagnosticCancellation;
    private CancellationTokenSource? _preflightCancellation;
    private ToolStripMenuItem _preflightMenuItem = null!;
    private ToolStripMenuItem _decodeCheckMenuItem = null!;
    private ToolStripMenuItem _preflightResultsMenuItem = null!;
    private ToolStripMenuItem? _runningPreflightMenuItem;
    private SoundCheckResult? _preflightResult;
    private const string PreflightCancelling = "中止中（現在のファイルの処理が終わるまで待機）";
    private ToolStripButton _cancelDiagnosticButton = null!;
    private (DateTimeOffset WallTime, long Timestamp)? _previousClockTick;
    private bool _systemSuspended;
    private bool _interactiveResize;
    private Task _resumeOutputCheck = Task.CompletedTask;

    public MainForm(
        ProjectManager projectManager,
        IAudioEngine audioEngine,
        AudioDeviceManager deviceManager,
        TriggerEngine triggerEngine,
        IAppLogger logger,
        string logDirectory,
        RecentProjectStore? recentProjects = null)
        : this(projectManager, audioEngine, deviceManager, triggerEngine, logger, logDirectory, recentProjects, null)
    {
    }

    internal MainForm(ProjectManager projectManager, IAudioEngine audioEngine, AudioDeviceManager deviceManager,
        TriggerEngine triggerEngine, IAppLogger logger, string logDirectory,
        RecentProjectStore? recentProjects, UiPreferences? uiPreferences, OutputDevicePreferences? outputPreferences = null)
    {
        UiTheme.ConfigureApplicationColors(projectManager.Current.Settings.DarkMode);
        _projectManager = projectManager;
        _globalHook = new GlobalKeyboardHook(() => Interlocked.Read(ref _playbackGeneration));
        _audioEngine = audioEngine;
        _deviceManager = deviceManager;
        _triggerEngine = triggerEngine;
        _logger = logger;
        _outputPreferences = outputPreferences;
        _outputPreference = outputPreferences?.Load(logger) ?? default;
        _logDirectory = Path.GetFullPath(logDirectory);
        _uiPreferences = uiPreferences;
        _requestedHeaderHeight = uiPreferences?.HeaderHeight;
        _recentProjects = recentProjects ?? new RecentProjectStore(Path.Combine(_logDirectory, "recent-projects.json"));

        Text = "pondex";
        MinimumSize = new Size(500, 320);
        ClientSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        DpiWindowHelper.Attach(this);
        KeyPreview = false;
        AllowDrop = true;

        var menu = BuildMenu(out _recentMenu, out _muteMenuItem);
        MainMenuStrip = menu;
        _recentMenu.DropDownOpening += (_, _) => RefreshRecentMenu();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 9));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(menu, 0, 0);

        var header = BuildHeader(
            out _projectLabel,
            out _deviceLabel,
            out _clockLabel,
            out _clockModeLabel,
            out _masterVolume,
            out _masterMute);
        root.Controls.Add(header, 0, 1);
        _deviceLabel.Click += async (_, _) => await ShowSettings();
        _deviceLabel.Cursor = Cursors.Hand;
        _deviceLabel.AccessibleDescription = "クリックして出力デバイスの設定を開く";
        _headerGrip = new HeaderResizeGrip();
        root.Controls.Add(_headerGrip, 0, 2);
        _headerToolTip.SetToolTip(_headerGrip, "ドラッグで高さを調整。ダブルクリックで標準に戻します。");
        _headerGrip.ResizedBy += delta =>
        {
            _dragHeaderHeight = (int)root.RowStyles[1].Height + delta;
            ApplyHeaderHeight(root);
        };
        _headerGrip.ResizeFinished += () =>
        {
            if (_dragHeaderHeight is null) { return; }
            _requestedHeaderHeight = (int)Math.Round(root.RowStyles[1].Height * 96d / root.DeviceDpi);
            _dragHeaderHeight = null;
            SaveHeaderHeight();
        };
        _headerGrip.ResetRequested += () =>
        {
            _dragHeaderHeight = null;
            _requestedHeaderHeight = null;
            ApplyHeaderHeight(root);
            root.PerformLayout();
            SaveHeaderHeight();
        };
        root.SizeChanged += (_, _) => ApplyHeaderHeight(root);
        root.DpiChangedAfterParent += (_, _) =>
        {
            root.RowStyles[2].Height = HeaderHeight.Scale(9, root.DeviceDpi);
            ApplyHeaderHeight(root);
        };

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
        var triggersTab = new TabPage("イベント") { Padding = new Padding(4) };
        _triggerPanel = new TriggerPanel { Dock = DockStyle.Fill };
        _triggerPanel.DefinitionsChanged += (_, _) =>
        {
            _triggerEngine.SetTriggers(_projectManager.Current.Triggers, preserveRuntimeState: true);
            _projectManager.MarkDirty(shortcutBindingsChanged: false);
        };
        _triggerPanel.OperationCompleted += (message, warning) => SetStatus(message, warning);
        triggersTab.Controls.Add(_triggerPanel);
        _tabs.TabPages.Add(soundsTab);
        _tabs.TabPages.Add(triggersTab);
        root.Controls.Add(_tabs, 0, 3);

        var statusStrip = new StatusStrip
        {
            ShowItemToolTips = true,
            SizingGrip = false,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(245, 247, 249),
            RenderMode = ToolStripRenderMode.System,
            Padding = new Padding(6, 2, 6, 2)
        };
        _statusLabel = new ToolStripStatusLabel("準備完了")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.ControlText,
            BorderSides = ToolStripStatusLabelBorderSides.Top,
            BorderStyle = Border3DStyle.Etched
        };
        statusStrip.Items.Add(_statusLabel);
        var emergencyButton = new ToolStripButton("緊急停止")
        {
            Tag = UiTheme.DangerButtonRole,
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Font = UiTheme.CreateFont(10F, FontStyle.Bold),
            ToolTipText = "Ctrl+Shift+Rで全音源を緊急停止し、イベントも休止します。",
            AccessibleName = "緊急停止"
        };
        emergencyButton.Click += (_, _) => QueueEmergencyStop();
        statusStrip.Items.Add(emergencyButton);
        _cancelDiagnosticButton = new ToolStripButton("診断をキャンセル") { Visible = false };
        _cancelDiagnosticButton.Click += (_, _) =>
        {
            StopForEmergency(invalidateConfiguration: false);
            SetStatus("動作診断をキャンセルしました。イベント休止中", false);
        };
        statusStrip.Items.Add(_cancelDiagnosticButton);
        _resumeTriggersButton = new ToolStripButton("イベント再開") { DisplayStyle = ToolStripItemDisplayStyle.Text, Visible = false };
        _resumeTriggersButton.Click += async (_, _) =>
        {
            if (_manualEmergencyStopping) { return; }
            var check = _resumeOutputCheck;
            await check;
            if (IsDisposed || Disposing || _manualEmergencyStopping || _systemSuspended || check != _resumeOutputCheck
                || _diagnosticCancellation is not null) { return; }
            if (_outputUnavailable) { SetStatus("設定で出力先を確認してから再開してください。", true); return; }
            _triggerEngine.Resume(DateTimeOffset.Now, _stopwatch.Elapsed, _stopwatch.IsRunning);
            _resumeTriggersButton.Visible = false;
            if (_projectManager.Current.Settings.SilentLoop)
            {
                _outputConfigurationTask = ApplySilentLoopAsync(_projectManager.Current.Settings, _outputConfigurationVersion);
            }
            if (_logger.DetailedLoggingEnabled) { _logger.Debug("automatic triggers resumed"); }
            SetStatus("自動イベントを再開しました。", false);
        };
        statusStrip.Items.Add(_resumeTriggersButton);
        _editLockButton = new ToolStripDropDownButton("ロック: OFF")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "設定と音量の変更をロックします。再生・停止は使用できます。",
            AccessibleName = "編集ロック"
        };
        _enableEditLockItem = new ToolStripMenuItem("ロックする", null, (_, _) => SetEditingLocked(true));
        _disableEditLockItem = new ToolStripMenuItem("ロックを解除", null, (_, _) => SetEditingLocked(false)) { Enabled = false };
        _editLockButton.DropDownItems.AddRange([_enableEditLockItem, _disableEditLockItem]);
        statusStrip.Items.Add(_editLockButton);
        root.Controls.Add(statusStrip, 0, 4);
        statusStrip.SizeChanged += (_, _) => ApplyHeaderHeight(root);
        menu.SizeChanged += (_, _) => ApplyHeaderHeight(root);
        Controls.Add(root);

        _shortcutDispatcher = new ShortcutDispatcher(
            new ShortcutManager(),
            () => _projectManager.Current.Sounds,
            () => _projectManager.Current.GroupShortcuts,
            IsEditingText,
            ExecuteShortcuts);
        _backgroundShortcutDispatcher = new ShortcutDispatcher(
            new ShortcutManager(),
            () => _projectManager.Current.Sounds,
            () => _projectManager.Current.GroupShortcuts,
            IsEditingText,
            ExecuteShortcuts);
        _messageFilter = new ShortcutMessageFilter(HandleWindowShortcut,
            hwnd => Control.FromChildHandle(hwnd)?.FindForm() is { } form && IsOwnedWindow(form));
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
        ResizeBegin += (_, _) => _interactiveResize = true;
        Resize += (_, _) => { if (!_interactiveResize) { ResizeSoundCards(); } };
        ResizeEnd += (_, _) =>
        {
            _interactiveResize = false;
            ResizeSoundCards();
        };
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
        _saveCommands.AddRange([saveItem, saveAsItem]);
        newItem.ShortcutKeys = Keys.Control | Keys.N;
        openItem.ShortcutKeys = Keys.Control | Keys.O;
        saveItem.ShortcutKeys = Keys.Control | Keys.S;
        saveAsItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.S;
        var addItem = new ToolStripMenuItem("音源を追加…", null, (_, _) => AddSoundsWithDialog());
        recentMenu = new ToolStripMenuItem("最近使ったファイル");
        _missingAudioMenuItem = new ToolStripMenuItem("欠落音源…", null, (_, _) => ShowMissingSounds()) { Visible = false };
        file.DropDownItems.AddRange([newItem, openItem, saveItem, saveAsItem, new ToolStripSeparator(), addItem, _missingAudioMenuItem, recentMenu]);

        var operations = new ToolStripMenuItem("操作(&O)");
        var stopAllItem = new ToolStripMenuItem("全停止(&R)", null, (_, _) => StopAll())
        {
            ShortcutKeyDisplayString = "Ctrl+R"
        };
        muteMenuItem = new ToolStripMenuItem("ミュート(&M)", null, (_, _) => ToggleMasterMute());
        var groupShortcuts = new ToolStripMenuItem("グループキー(&G)…", null, (_, _) => ShowGroupShortcuts());
        var soundOrder = new ToolStripMenuItem("並び替え(&L)…", null, (_, _) => ShowSoundOrder());
        var diagnostics = new ToolStripMenuItem("動作診断(&D)…", null, async (_, _) => await RunSelfTestAsync());
        _preflightMenuItem = new ToolStripMenuItem("音源ファイルの読み取り確認…", null, async (_, _) => await ToggleSoundPreflightAsync(SoundCheckMode.Read));
        _decodeCheckMenuItem = new ToolStripMenuItem("音源のデコード確認…", null, async (_, _) => await ToggleSoundPreflightAsync(SoundCheckMode.Decode));
        _preflightResultsMenuItem = new ToolStripMenuItem("音源の確認結果…", null, (_, _) => ShowSoundPreflightResult()) { Enabled = false };
        var timings = new ToolStripMenuItem("再生開始を計測") { CheckOnClick = true };
        timings.CheckedChanged += (_, _) =>
        {
            _audioEngine.PlaybackTimingEnabled = timings.Checked;
            SetStatus(timings.Checked
                ? "再生開始の内部処理時間を計測します。実際の出音遅延ではありません。"
                : "再生開始の計測を終了しました。", false);
        };
        var logLocation = new ToolStripMenuItem("ログの保存先…", null, (_, _) => ShowLogLocation()) { Enabled = _uiPreferences is not null };
        var emergencyStop = new ToolStripMenuItem("緊急停止", null, (_, _) => QueueEmergencyStop()) { Name = "emergencyStopMenuItem", ShortcutKeyDisplayString = "Ctrl+Shift+R" };
        operations.DropDownItems.AddRange(
            [stopAllItem, muteMenuItem, new ToolStripSeparator(), soundOrder, groupShortcuts,
                new ToolStripSeparator(), diagnostics, _preflightMenuItem, _decodeCheckMenuItem, _preflightResultsMenuItem, timings, logLocation, new ToolStripSeparator(), emergencyStop]);

        var settings = new ToolStripMenuItem("設定(&S)", null, async (_, _) => await ShowSettings());
        var help = new ToolStripMenuItem("ヘルプ(&H)");
        var usage = new ToolStripMenuItem("使い方(&H)…", null, (_, _) => ShowHelp());
        help.DropDownItems.Add(usage);
        _editLockMenuItem = new ToolStripMenuItem("編集ロック", null, (_, _) => SetEditingLocked(!_editingLocked));
        operations.DropDownItems.Add(new ToolStripSeparator());
        operations.DropDownItems.Add(_editLockMenuItem);
        _editingCommands.AddRange([newItem, openItem, addItem, recentMenu, soundOrder, groupShortcuts, settings, diagnostics, muteMenuItem]);
        menu.Items.AddRange([file, operations, settings, help]);
        return menu;
    }

    private void ApplyHeaderHeight(TableLayoutPanel root)
    {
        if (_minimumHeaderHeight == 0 || root.IsDisposed || root.RowCount != 5) { return; }
        static int RowHeight(Control? control) => control is null ? 0 : control.Height + control.Margin.Vertical;
        var reserved = RowHeight(root.GetControlFromPosition(0, 0)) + RowHeight(root.GetControlFromPosition(0, 4))
            + (int)root.RowStyles[2].Height + HeaderHeight.Scale(112, root.DeviceDpi)
            + (root.GetControlFromPosition(0, 3)?.Margin.Vertical ?? 0) + root.Padding.Vertical;
        var requested = _dragHeaderHeight is { } drag
            ? (int)Math.Round(drag * 96d / root.DeviceDpi) : _requestedHeaderHeight;
        if (_dragHeaderHeight is null && IsHandleCreated && WindowState == FormWindowState.Normal)
        {
            var measured = _minimumHeaderHeight + reserved + Height - ClientSize.Height;
            var workingHeight = Screen.FromControl(this).WorkingArea.Height;
            var minHeight = Math.Min(workingHeight, Math.Max(HeaderHeight.Scale(320, root.DeviceDpi), measured));
            if (MinimumSize.Height != minHeight) { MinimumSize = new Size(MinimumSize.Width, minHeight); }
        }
        var height = HeaderHeight.Clamp(requested, _minimumHeaderHeight, root.ClientSize.Height, reserved, root.DeviceDpi);
        if (root.RowStyles[1].Height != height) { root.RowStyles[1].Height = height; }
    }

    private void SaveHeaderHeight() => _uiPreferences?.QueueHeaderHeight(_requestedHeaderHeight, exception =>
    {
        _logger.Error("ヘッダーの高さを保存できませんでした。", exception);
        TryBeginInvoke(() => SetStatus("ヘッダーの高さを保存できませんでした。", true));
    });

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
            Padding = new Padding(6, 3, 6, 3),
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

        projectLabel = HeaderLabel("新しいプロジェクト", 11, FontStyle.Bold);
        deviceLabel = HeaderLabel("出力: Windows 既定", 10, FontStyle.Regular);
        clockModeLabel = HeaderLabel("時計", 10, FontStyle.Bold);
        clockLabel = HeaderLabel("00:00:00", 16, FontStyle.Bold);
        clockLabel.TextAlign = ContentAlignment.MiddleCenter;
        clockLabel.AutoEllipsis = false;

        var addButton = HeaderButton("＋ 音源追加", Color.FromArgb(225, 235, 245));
        _addSoundButton = addButton;
        var stopAllButton = HeaderButton("■ 全停止", Color.FromArgb(220, 65, 65));
        _stopAllButton = stopAllButton;
        addButton.Tag = UiTheme.AccentButtonRole;
        stopAllButton.Tag = UiTheme.StopButtonRole;
        stopAllButton.ForeColor = Color.White;
        stopAllButton.Font = new Font(Font.FontFamily, 14, FontStyle.Bold);
        var modeButton = HeaderButton("時計 / SW", SystemColors.Control);
        var startButton = HeaderButton("開始", SystemColors.Control);
        _stopwatchToggleButton = startButton;
        var resetButton = HeaderButton("SWリセット", SystemColors.Control);

        _headerToolTip.SetToolTip(modeButton, "時計とストップウォッチの表示を切り替えます。");
        _headerToolTip.SetToolTip(startButton, "ストップウォッチを開始・一時停止します。");
        _headerToolTip.SetToolTip(resetButton, "ストップウォッチを0に戻し、SWイベントを再び実行可能にします。");
        _headerToolTip.SetToolTip(stopAllButton, "すべての音源を停止します。フェードの有無は設定で変更できます。");

        var masterVolumeControl = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            TickStyle = TickStyle.None,
            AutoSize = false,
            Value = 100,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = 25
        };
        masterVolume = masterVolumeControl;
        var masterVolumeValue = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100,
            DecimalPlaces = 0,
            Increment = 1,
            Value = 100,
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = HorizontalAlignment.Right,
            Margin = new Padding(3)
        };
        _masterVolumeValue = masterVolumeValue;
        VolumeContextMenu.Attach(masterVolumeValue, masterVolumeControl);
        _headerToolTip.SetToolTip(masterVolumeControl, "0～100%。マスター音量を調整します。");
        _headerToolTip.SetToolTip(masterVolumeValue, "0～100%。右クリックで100%（標準）へ戻せます。");
        var masterMuteControl = new CheckBox
        {
            Text = "ミュート",
            ForeColor = Color.White,
            Appearance = Appearance.Button,
            AutoSize = false,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = UiTheme.CreateFont(10F, FontStyle.Bold),
            BackColor = Color.FromArgb(58, 63, 68),
            UseVisualStyleBackColor = false,
            Margin = new Padding(4, 2, 4, 2)
        };
        masterMute = masterMuteControl;
        var masterPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
        masterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        masterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        masterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        masterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        masterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        masterPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var masterCaption = HeaderLabel("MASTER", 8.5F, FontStyle.Bold);
        masterCaption.AutoEllipsis = false;
        masterCaption.AutoSize = true;
        masterCaption.Dock = DockStyle.None;
        masterCaption.Anchor = AnchorStyles.Left;
        masterCaption.UseCompatibleTextRendering = false;
        masterPanel.Controls.Add(masterCaption, 0, 0);
        masterPanel.Controls.Add(masterVolumeControl, 1, 0);
        masterPanel.Controls.Add(masterVolumeValue, 2, 0);
        var masterUnit = HeaderLabel("%", 9F, FontStyle.Bold);
        masterUnit.AutoSize = true;
        masterUnit.AutoEllipsis = false;
        masterUnit.Dock = DockStyle.None;
        masterUnit.Anchor = AnchorStyles.Left;
        masterPanel.Controls.Add(masterUnit, 3, 0);
        masterPanel.Controls.Add(masterMuteControl, 4, 0);

        header.Controls.Add(projectLabel, 0, 0);
        header.Controls.Add(deviceLabel, 0, 1);
        header.Controls.Add(masterPanel, 1, 0);
        header.SetRowSpan(masterPanel, 2);
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
        var compactStopwatchButtons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        compactStopwatchButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        compactStopwatchButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
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
        var arrangingHeader = false;
        var arrangedWidth = -1;
        var arrangedDpi = -1;
        void ArrangeHeader(bool force = false)
        {
            if (arrangingHeader || header.IsDisposed || header.Disposing
                || (!force && arrangedWidth == header.ClientSize.Width && arrangedDpi == header.DeviceDpi)) { return; }
            arrangingHeader = true;
            arrangedWidth = header.ClientSize.Width;
            arrangedDpi = header.DeviceDpi;
            try
            {
                var scale = header.DeviceDpi / 96F;
                // Nested absolute columns can keep their original pixel widths after DPI/font
                // changes. Reserve the full text and the actual up/down button width instead.
                var digits = new string('8', masterVolumeValue.Maximum.ToString("F0", CultureInfo.InvariantCulture).Length);
                var textWidth = TextRenderer.MeasureText(digits, masterVolumeValue.Font, Size.Empty, TextFormatFlags.SingleLine).Width;
                var spinnerWidth = masterVolumeValue.Controls.Cast<Control>()
                    .Where(control => control is not TextBoxBase).Select(control => control.Width).DefaultIfEmpty(0).Max();
                var numericWidth = textWidth + Math.Max(spinnerWidth, (int)Math.Ceiling(20 * scale)) + (int)Math.Ceiling(8 * scale);
                masterVolumeValue.MinimumSize = new Size(numericWidth, 0);
                masterVolumeValue.Width = numericWidth;
                masterVolumeControl.Height = HeaderHeight.Scale(25, header.DeviceDpi);
                masterMuteControl.Height = Math.Max(HeaderHeight.Scale(25, header.DeviceDpi),
                    TextRenderer.MeasureText("ミュート中", masterMuteControl.Font).Height + HeaderHeight.Scale(4, header.DeviceDpi));
                var sliderWidth = (int)Math.Ceiling(80 * scale);
                var muteWidth = TextRenderer.MeasureText("ミュート中", masterMuteControl.Font,
                    Size.Empty, TextFormatFlags.SingleLine).Width + (int)Math.Ceiling(16 * scale);
                var muteColumnWidth = Math.Max(muteWidth, masterMuteControl.GetPreferredSize(Size.Empty).Width)
                    + masterMuteControl.Margin.Horizontal;
                masterPanel.ColumnStyles[4].Width = muteColumnWidth;
                masterPanel.MinimumSize = new Size(
                    masterCaption.GetPreferredSize(Size.Empty).Width + masterCaption.Margin.Horizontal
                    + numericWidth + masterVolumeValue.Margin.Horizontal
                    + masterUnit.GetPreferredSize(Size.Empty).Width + masterUnit.Margin.Horizontal
                    + sliderWidth + masterVolumeControl.Margin.Horizontal
                    + muteColumnWidth + (int)Math.Ceiling(8 * scale), 0);
                // Measure text independently of the current cell width so switching
                // from a wide compact cell cannot increase its own breakpoint.
                static int RequiredWidth(Control control) => control.Text.Split('\n').Max(line =>
                    TextRenderer.MeasureText(line, control.Font, Size.Empty, TextFormatFlags.SingleLine).Width)
                    + control.Padding.Horizontal + control.Margin.Horizontal;
                var wideMasterWidth = Math.Max(360 * scale, masterPanel.MinimumSize.Width + masterPanel.Margin.Horizontal);
                var wideColumns = new float[] {
                    wideMasterWidth,
                    Math.Max(105 * scale, RequiredWidth(clockLabelControl)),
                    Math.Max(80 * scale, Math.Max(RequiredWidth(modeButton), RequiredWidth(startButton))),
                    Math.Max(85 * scale, Math.Max(RequiredWidth(resetButton), RequiredWidth(addButton))),
                    Math.Max(100 * scale, RequiredWidth(stopAllButton))
                };
                var wideMinimum = Math.Max((int)(1250 * scale),
                    (int)Math.Ceiling(wideColumns.Sum() + 125 * scale + header.Padding.Horizontal));
                var compact = header.ClientSize.Width < wideMinimum;
                static int RequiredHeight(Control control) => control.GetPreferredSize(Size.Empty).Height + control.Margin.Vertical;
                compactClock.RowStyles[0].Height = Math.Max(22 * scale, RequiredHeight(clockModeLabelControl));
                float[] compactHeights = [
                    Math.Max(22 * scale, Math.Max(RequiredHeight(projectLabelControl), RequiredHeight(deviceLabelControl))),
                    Math.Max(32 * scale, MasterRowHeight()),
                    Math.Max(42 * scale, Math.Max(compactClock.RowStyles[0].Height + RequiredHeight(clockLabelControl), RequiredHeight(modeButton))),
                    Math.Max(32 * scale, Math.Max(RequiredHeight(addButton), Math.Max(RequiredHeight(startButton), RequiredHeight(resetButton))))
                ];
                int MasterRowHeight() => Math.Max(
                    new Control[] { masterCaption, masterVolumeValue, masterUnit }.Max(RequiredHeight),
                    Math.Max(masterVolumeControl.Height + masterVolumeControl.Margin.Vertical,
                        masterMuteControl.Height + masterMuteControl.Margin.Vertical));
                var wideRowHeight = Math.Max(Math.Max(30 * scale, MasterRowHeight() / 2f), new Control[] {
                    clockLabelControl, clockModeLabelControl, modeButton, startButton, resetButton, addButton }.Max(RequiredHeight));
                _minimumHeaderHeight = (int)Math.Ceiling(compact ? compactHeights.Sum() : 2 * wideRowHeight)
                    + header.Padding.Vertical + header.Margin.Vertical;
                if (header.Parent is TableLayoutPanel parent) { ApplyHeaderHeight(parent); }
                if (compactLayout == compact)
                {
                    if (compact)
                    {
                        for (var row = 0; row < compactHeights.Length; row++) { header.RowStyles[row].Height = compactHeights[row]; }
                    }
                    else
                    {
                        for (var column = 0; column < wideColumns.Length; column++) { header.ColumnStyles[column + 1].Width = wideColumns[column]; }
                        header.RowStyles[0].Height = wideRowHeight;
                        header.RowStyles[1].Height = wideRowHeight;
                    }
                    return;
                }

                compactLayout = compact;
                header.SuspendLayout();
                header.Controls.Clear();
                header.ColumnStyles.Clear();
                header.RowStyles.Clear();
                header.ColumnCount = compact ? 3 : 6;
                header.RowCount = compact ? 4 : 2;
                if (compact)
                {
                    header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                    header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                    header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                    foreach (var height in compactHeights)
                    {
                        header.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                    }

                    header.Controls.Add(projectLabelControl, 0, 0);
                    header.SetColumnSpan(projectLabelControl, 2);
                    header.Controls.Add(deviceLabelControl, 2, 0);
                    header.Controls.Add(masterPanel, 0, 1);
                    header.SetColumnSpan(masterPanel, 3);
                    header.SetRowSpan(masterPanel, 1);
                    compactClock.Controls.Add(clockModeLabelControl, 0, 0);
                    compactClock.Controls.Add(clockLabelControl, 0, 1);
                    header.Controls.Add(compactClock, 0, 2);
                    header.Controls.Add(modeButton, 1, 2);
                    compactStopwatchButtons.Controls.Add(startButton, 0, 0);
                    compactStopwatchButtons.Controls.Add(resetButton, 1, 0);
                    header.Controls.Add(compactStopwatchButtons, 0, 3);
                    header.Controls.Add(addButton, 1, 3);
                    header.SetColumnSpan(addButton, 1);
                    header.Controls.Add(stopAllButton, 2, 2);
                    header.SetRowSpan(stopAllButton, 2);
                }
                else
                {
                    header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                    foreach (var width in wideColumns)
                    {
                        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
                    }

                    header.RowStyles.Add(new RowStyle(SizeType.Absolute, wideRowHeight));
                    header.RowStyles.Add(new RowStyle(SizeType.Absolute, wideRowHeight));
                    header.Controls.Add(projectLabelControl, 0, 0);
                    header.SetColumnSpan(projectLabelControl, 1);
                    header.Controls.Add(deviceLabelControl, 0, 1);
                    header.Controls.Add(masterPanel, 1, 0);
                    header.SetColumnSpan(masterPanel, 1);
                    header.SetRowSpan(masterPanel, 2);
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
            finally { arrangingHeader = false; }
        }

        foreach (var control in new Control[] { masterCaption, masterVolumeValue, masterUnit, masterMuteControl,
            clockLabelControl, clockModeLabelControl, projectLabelControl, deviceLabelControl, addButton, stopAllButton,
            modeButton, startButton, resetButton })
        {
            control.FontChanged += (_, _) => ArrangeHeader(force: true);
        }
        header.DpiChangedAfterParent += (_, _) => ArrangeHeader(force: true);
        header.SizeChanged += (_, _) => ArrangeHeader();
        header.ParentChanged += (_, _) => ArrangeHeader(force: true);
        header.HandleCreated += (_, _) => QueueHeaderLayout();
        header.Disposed += (_, _) => compactClock.Dispose();
        header.Disposed += (_, _) => compactStopwatchButtons.Dispose();
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
            var satisfied = _triggerEngine.SatisfiedStopwatchTriggerCount;
            if (satisfied > 0 && MessageBox.Show(this,
                $"ストップウォッチをリセットすると、成立済みのイベント{satisfied}件も再び待機します。リセットしますか？",
                "SWリセット", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) { return; }
            _stopwatch.Reset();
            _triggerEngine.ResetStopwatch();
            _stopwatchMode = true;
            UpdateClockDisplay();
        };
        masterVolumeControl.ValueChanged += (_, _) =>
        {
            if ((_editingLocked || _manualEmergencyStopping) && !_applyingProjectSettings) { RestoreMasterVolume(); return; }
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
            _projectManager.MarkDirty(shortcutBindingsChanged: false);
        };
        masterVolumeValue.ValueChanged += (_, _) =>
        {
            if ((_editingLocked || _manualEmergencyStopping) && !_applyingProjectSettings) { RestoreMasterVolume(); return; }
            var value = decimal.ToInt32(masterVolumeValue.Value);
            if (masterVolumeControl.Value != value)
            {
                masterVolumeControl.Value = value;
            }
        };
        masterMuteControl.CheckedChanged += (_, _) =>
        {
            if ((_editingLocked || _manualEmergencyStopping) && !_applyingProjectSettings)
            {
                masterMuteControl.Checked = _projectManager.Current.Settings.MasterMute;
                return;
            }
            UpdateMuteAppearance(masterMuteControl);
            _muteMenuItem.Checked = masterMuteControl.Checked;
            if (_applyingProjectSettings)
            {
                return;
            }

            _audioEngine.MasterMute = masterMuteControl.Checked;
            _projectManager.Current.Settings.MasterMute = masterMuteControl.Checked;
            _projectManager.MarkDirty(shortcutBindingsChanged: false);
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
        // Measure the caption and chrome independently of the button's current docked height.
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Fill,
        BackColor = color,
        UseVisualStyleBackColor = false,
        Margin = new Padding(4),
        Font = UiTheme.CreateFont(10F, FontStyle.Bold)
    };

    private bool CanEditProject()
    {
        if (_editingLocked)
        {
            SetStatus("編集ロック中です。変更するには画面下のロックを解除してください。", true);
            return false;
        }
        return !_manualEmergencyStopping && !_projectOperationInProgress && _diagnosticCancellation is null;
    }

    private void SetEditingLocked(bool locked)
    {
        if (_manualEmergencyStopping || _projectOperationInProgress || _diagnosticCancellation is not null || _editingLocked == locked) { return; }
        if (locked)
        {
            EndTextEditing();
            _ = _masterVolumeValue.Value;
        }
        _editingLocked = locked;
        if (_logger.DetailedLoggingEnabled) { _logger.Debug($"edit_lock changed: locked={locked}"); }
        ApplyEditingLock();
        UpdateProjectHeader();
        SetStatus(locked ? "設定・音量をロック中" : "ロックを解除しました", false,
            locked ? "再生・停止は使用できます。変更する場合は画面下のロックを解除してください。" : null);
    }

    private void ApplyEditingLock()
    {
        var editable = !_manualEmergencyStopping && !_editingLocked && !_projectOperationInProgress && _diagnosticCancellation is null;
        foreach (var command in _editingCommands) { command.Enabled = editable; }
        _addSoundButton.Enabled = editable;
        var masterEditable = !_manualEmergencyStopping && !_editingLocked && !_projectOperationInProgress;
        _masterVolume.Enabled = masterEditable;
        _masterVolumeValue.Enabled = masterEditable;
        _masterMute.Enabled = masterEditable;
        _triggerPanel.SetEditingLocked(!editable);
        foreach (var card in _soundCards.Values) { card.SetEditingLocked(!editable); }
        _editLockButton.Enabled = !_projectOperationInProgress && _diagnosticCancellation is null;
        _editLockButton.Text = _editingLocked ? "ロック: ON" : "ロック: OFF";
        _editLockMenuItem.Checked = _editingLocked;
        _enableEditLockItem.Enabled = !_editingLocked;
        _disableEditLockItem.Enabled = _editingLocked;
    }

    private void RestoreMasterVolume()
    {
        _applyingProjectSettings = true;
        try
        {
            var percent = ProjectLimits.ToVolumePercent(_projectManager.Current.Settings.MasterVolume, 1);
            _masterVolume.Value = percent;
            _masterVolumeValue.Value = percent;
        }
        finally { _applyingProjectSettings = false; }
    }

    private void ResetProjectRuntime()
    {
        CancelSoundPreflight();
        _preflightResult = null;
        _preflightResultsMenuItem.Enabled = false;
        var wasSuspended = _triggerEngine.IsSuspended;
        InvalidatePendingPlayback();
        _triggerEngine.Suspend();
        _audioEngine.StopAll();
        _stopwatch.Reset();
        _triggerEngine.SetTriggers(_projectManager.Current.Triggers);
        if (!wasSuspended) { _triggerEngine.Resume(DateTimeOffset.Now, _stopwatch.Elapsed, _stopwatch.IsRunning); }
        _resumeTriggersButton.Visible = wasSuspended;
        UpdateClockDisplay();
    }

    private async Task NewProjectAsync()
    {
        if (!CanEditProject()) { return; }
        if (!await EnsureCanReplaceProjectAsync())
        {
            return;
        }

        ResetProjectRuntime();
        _projectManager.New();
        ApplyProjectSettings();
        RebuildSoundCards();
        SetStatus("新規プロジェクト", false);
    }

    private async Task OpenWithDialogAsync()
    {
        if (!CanEditProject()) { return; }
        using var dialog = new OpenFileDialog
        {
            Filter = "pondexプロジェクト (*.pondex)|*.pondex|すべてのファイル (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await OpenProjectAsync(dialog.FileName);
        }
    }

    private async Task OpenProjectAsync(string path)
    {
        if (!CanEditProject())
        {
            return;
        }

        if (!await EnsureCanReplaceProjectAsync())
        {
            return;
        }

        CancelSoundPreflight();
        try
        {
            SetProjectOperationBusy(true);
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"project open requested: path={path}"); }
            var opened = await _projectManager.OpenAsync(path, AuthorizeUntrustedProject);
            if (!opened)
            {
                if (_logger.DetailedLoggingEnabled) { _logger.Debug($"project open cancelled: path={path}"); }
                SetStatus("プロジェクトの読み込みをキャンセルしました。", false);
                return;
            }

            await _manualEmergencyStopTask;
            ResetProjectRuntime();
            await Task.Run(() => _recentProjects.Add(path));
            _logger.Info($"プロジェクトを開きました: {path}");
            ApplyProjectSettings();
            if (_logger.DetailedLoggingEnabled)
            {
                _logger.Debug($"project opened: path={path} missing={_projectManager.Current.MissingFiles.Count} warnings={_projectManager.Current.LoadWarnings.Count} requires_save_as={_projectManager.RequiresSaveAs}");
            }
            await RebuildSoundCardsAsync();
            if (_projectManager.Current.MissingFiles.Count > 0)
            {
                MessageBox.Show(this,
                    $"音源が{_projectManager.Current.MissingSoundIds.Count}件見つかりません。ファイルメニューの「欠落音源」から確認・差し替えできます。",
                    "欠落音源", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshRecentMenu();
            var imported = _projectManager.Current.IsPonDashiImport;
            if (_projectManager.Current.LoadWarnings.Count > 0)
            {
                var warnings = _projectManager.Current.LoadWarnings;
                var warningText = string.Join(Environment.NewLine, warnings.Take(10));
                if (warnings.Count > 10) { warningText += $"\nほか{warnings.Count - 10}件。詳細はログを確認してください。"; }
                _logger.Warning($"プロジェクトの読み込み時の確認事項:{Environment.NewLine}{string.Join(Environment.NewLine, warnings)}");
                MessageBox.Show(
                    this,
                    imported
                        ? $"無印ポン出しくんから読み込みました。保存形式は .pondex です。変換前の元ファイルも保管してください。\n\n{warningText}"
                        : $"読み込めた項目は使えます。変更を残すには、別の名前で保存してください。元のファイルは上書きできません。\n\n{warningText}",
                    imported ? "無印ポン出しくん 読み込み結果" : "一部の設定を読み込めませんでした",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                SetStatus(imported ? $"無印プロジェクトを読み込みました（確認事項{warnings.Count}件）。"
                    : $"警告付きで開きました（{warnings.Count}件）。", true);
            }
            else
            {
                var missing = _projectManager.Current.MissingSoundIds.Count;
                SetStatus(imported
                    ? $"無印プロジェクトを読み込みました。.pondex で保存し、元のファイルも保管してください。{(missing > 0 ? $" 欠落音源{missing}件。" : "")}"
                    : missing == 0 ? "読み込み完了" : $"読み込み完了・欠落{missing}件", missing > 0);
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
        if (_projectOperationInProgress || _diagnosticCancellation is not null)
        {
            SetStatus("別のプロジェクト操作が完了するまで待ってください。", true);
            return false;
        }

        var timing = _logger.DetailedLoggingEnabled ? new OperationTiming() : null;
        var savingProject = _projectManager.Current;
        var soundCount = savingProject.Sounds.Count;
        var saveAs = forceSaveAs || _projectManager.CurrentPath is null || _projectManager.RequiresSaveAs;
        try
        {
            using (timing?.Measure("ui_commit"))
            {
                EndTextEditing();
                _ = _masterVolumeValue.Value;
                _savingProject = true;
                SetProjectOperationBusy(true);
            }
            if (saveAs)
            {
                using var dialog = new SaveFileDialog
                {
                    Filter = "pondexプロジェクト (*.pondex)|*.pondex",
                    DefaultExt = "pondex",
                    AddExtension = true,
                    OverwritePrompt = false,
                    FileName = SanitizeFileName(_projectManager.Current.Project.Name) + ".pondex"
                };
                using (timing?.Measure("file_dialog"))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) { return false; }
                }

                ProjectSaveDestination destination;
                using (timing?.Measure("destination_path"))
                {
                    destination = await ProjectSaveDestination.CaptureAsync(dialog.FileName);
                }
                if (destination.Revision is not null)
                {
                    using var confirmation = timing?.Measure("overwrite_confirmation");
                    if (MessageBox.Show(this, $"次のファイルを上書きしますか？\n{destination.Path}", "上書き確認",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        return false;
                    }
                }
                await _projectManager.SaveAsAsync(destination, timing);
            }
            else
            {
                await _projectManager.SaveAsync(timing);
            }

            if (_projectManager.CurrentPath is { } savedPath)
            {
                using (timing?.Measure("recent"))
                {
                    await Task.Run(() => _recentProjects.Add(savedPath));
                }
            }
            if (!ReferenceEquals(savingProject, _projectManager.Current) || _projectManager.IsDirty)
            {
                if (timing is not null) { timing.Outcome = "saved_dirty"; }
                SetStatus("保存中の変更が残っています。もう一度保存してください。終了・切替は中止しました。", true);
                return false;
            }

            using var uiFeedback = timing?.Measure("ui_feedback");
            _logger.Info($"プロジェクトを保存しました: {_projectManager.CurrentPath}");
            if (timing is not null) { timing.Outcome = "saved"; }
            if (_projectManager.LastSaveResult is { BackupWarning: { } warning } result)
            {
                if (timing is not null) { timing.Outcome = "saved_backup_warning"; }
                _logger.Warning($"{warning} 復旧用バックアップ: {result.RecoveryBackupPath}");
                SetStatus($"保存しました（バックアップ整理に失敗）。復旧用: {result.RecoveryBackupPath}", true);
            }
            else
            {
                SetStatus("保存しました。", false);
            }
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception)
        {
            if (timing is not null) { timing.Outcome = "failed"; }
            _logger.Error("プロジェクトを保存できません。", exception);
            SetStatus($"保存できません: {exception.Message}", true);
            return false;
        }
        finally
        {
            using (timing?.Measure("ui_unlock"))
            {
                _savingProject = false;
                SetProjectOperationBusy(false);
            }
            if (timing is not null)
            {
                _logger.Debug($"save_timing mode={(saveAs ? "save_as" : "save")} destination={timing.Destination} bytes={timing.Bytes} sounds={soundCount} {timing.Format()}");
            }
        }
    }

    private void AddSoundsWithDialog()
    {
        if (!CanEditProject())
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
        if (!CanEditProject())
        {
            return;
        }

        var added = new List<SoundDefinition>();
        var skipped = new List<string>();
        foreach (var path in paths)
        {
            if (!IsSupportedAudioFile(path))
            {
                skipped.Add($"{Path.GetFileName(path)}（未対応形式）");
                continue;
            }

            try
            {
                added.Add(_projectManager.AddSound(path));
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                _logger.Warning($"音源を追加できません: {path} ({exception.Message})");
                skipped.Add($"{Path.GetFileName(path)}（{exception.Message}）");
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

        }
        if (skipped.Count > 0)
        {
            SetStatus($"追加{added.Count}件・スキップ{skipped.Count}件", true, string.Join(Environment.NewLine, skipped));
        }
        else if (added.Count > 0) { SetStatus($"音源を{added.Count}件追加しました。", false); }
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
        card.StopRequested += (_, _) => StopSound(sound.Id);
        card.PauseRequested += (_, _) => PauseSound(sound.Id);
        card.ResumeRequested += (_, _) =>
        {
            if (!_manualEmergencyStopping) { _audioEngine.Resume(sound.Id); }
        };
        card.FileBrowseRequested += (_, _) => BrowseReplacementFile(sound, card);
        card.DeleteRequested += (_, _) => DeleteSound(sound, card);
        card.InvalidKeyRejected += (_, _) =>
            SetStatus("キー名を確認してください。元の割り当てはそのままです。", true);
        card.GroupChangeCommitted += (_, eventArgs) => OnSoundGroupChangeCommitted(card, eventArgs);
        card.VolumeFadeRequested += (_, eventArgs) =>
        {
            if (!_manualEmergencyStopping) { _audioEngine.FadeVolume(sound.Id, eventArgs.TargetVolume, eventArgs.Duration); }
            _projectManager.MarkDirty(shortcutBindingsChanged: false);
        };
        var displayName = sound.Name;
        var assignedKey = sound.Key;
        var assignedGroup = sound.Group;
        card.DefinitionChanged += (_, _) =>
        {
            if (!_manualEmergencyStopping) { _audioEngine.ApplyLiveSettings(sound); }
            if (!string.Equals(displayName, sound.Name, StringComparison.Ordinal))
            {
                displayName = sound.Name;
                _triggerPanel.RefreshSoundNames();
            }
            var bindingsChanged = assignedKey != sound.Key || assignedGroup != sound.Group;
            if (assignedGroup != sound.Group) { _triggerPanel.RefreshSoundNames(); }
            assignedKey = sound.Key;
            assignedGroup = sound.Group;
            _projectManager.MarkDirty(shortcutBindingsChanged: bindingsChanged);
        };
        card.SetEditingLocked(_manualEmergencyStopping || _editingLocked || _projectOperationInProgress);
        card.UpdateState(_projectManager.Current.MissingSoundIds.Contains(sound.Id)
            ? PlaybackState.Missing : _manualEmergencyStopping ? PlaybackState.Stopped : _audioEngine.GetState(sound.Id));
        _soundCards[sound.Id] = card;
        _soundList.Controls.Add(card);
    }

    private void ShowSoundOrder()
    {
        if (!CanEditProject())
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
        if (!CanEditProject()) { return; }
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

        StopSound(sound.Id, immediately: true);
        var result = RemoveSoundDefinition(_projectManager.Current, sound.Id);
        if (!result.Removed)
        {
            return;
        }

        _soundCards.Remove(sound.Id);
        _projectManager.Current.MissingSoundIds.Remove(sound.Id);
        _projectManager.Current.ApprovedSoundPaths.Remove(sound.Id);
        _uiPlayVersions.Remove(sound.Id);
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
        if (_soundCards.Values.All(card => card.Width == width)) { return; }
        _soundList.SuspendLayout();
        try
        {
            foreach (var card in _soundCards.Values)
            {
                if (card.Width != width) { card.Width = width; }
            }
        }
        finally { _soundList.ResumeLayout(performLayout: true); }
    }

    private void ShowMissingSounds()
    {
        using var dialog = new Form
        {
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(720, 380),
            MinimumSize = new Size(420, 240)
        };
        dialog.AutoScaleMode = AutoScaleMode.Dpi;
        DpiWindowHelper.Attach(dialog);
        var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, DisplayMember = nameof(MissingSoundEntry.Description) };
        var replace = new Button { Text = "参照…", AutoSize = true };
        var close = new Button { Text = "閉じる", DialogResult = DialogResult.Cancel, AutoSize = true };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8) };
        footer.Controls.AddRange([replace, close]);
        dialog.Controls.Add(list);
        dialog.Controls.Add(footer);
        dialog.CancelButton = close;

        void RefreshList()
        {
            var project = _projectManager.Current;
            var missing = project.Sounds.Where(sound => project.MissingSoundIds.Contains(sound.Id))
                .Select(sound => new MissingSoundEntry(sound, $"{sound.Name} — {sound.File}")).ToArray();
            list.DataSource = missing;
            dialog.Text = $"欠落音源（残り{missing.Length}件）";
            replace.Enabled = !_editingLocked && list.SelectedItem is MissingSoundEntry;
        }
        list.SelectedIndexChanged += (_, _) => replace.Enabled = !_editingLocked && list.SelectedItem is MissingSoundEntry;
        replace.Click += (_, _) =>
        {
            if (list.SelectedItem is not MissingSoundEntry item || !_soundCards.TryGetValue(item.Sound.Id, out var card)) { return; }
            BrowseReplacementFile(item.Sound, card, dialog);
            RefreshList();
        };

        RefreshList();
        UiTheme.Apply(dialog, _darkMode);
        dialog.ShowDialog(this);
    }

    private sealed record MissingSoundEntry(SoundDefinition Sound, string Description);

    private void BrowseReplacementFile(SoundDefinition sound, SoundCard card, IWin32Window? owner = null)
    {
        if (!CanEditProject()) { return; }
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

        if (dialog.ShowDialog(owner ?? this) != DialogResult.OK || !CanEditProject())
        {
            return;
        }

        try
        {
            if (!IsSupportedAudioFile(dialog.FileName))
            {
                throw new InvalidOperationException("対応する音声形式を選んでください。");
            }
            _projectManager.ReplaceSoundFile(sound, dialog.FileName);
            StopSound(sound.Id, immediately: true);
            _projectManager.Current.MissingSoundIds.Remove(sound.Id);
            card.UpdateState(PlaybackState.Stopped);
            card.UpdateFilePath(sound.File);
            _triggerPanel.RefreshSoundNames();
            UpdateProjectHeader();
            SetStatus($"音声ファイルを差し替えました: {sound.Name}", false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            _logger.Warning($"音源を差し替えられません: {dialog.FileName} ({exception.Message})");
            MessageBox.Show(this, exception.Message, "音源差し替えエラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ToggleSoundPreflightAsync(SoundCheckMode mode)
    {
        if (_preflightCancellation is not null)
        {
            CancelSoundPreflight();
            return;
        }
        if (_manualEmergencyStopping || _projectOperationInProgress || _diagnosticCancellation is not null
            || _systemSuspended || _pendingPlayback.Count > 0) { return; }

        var paths = _projectManager.SnapshotPlaybackPaths();
        if (paths.Length == 0)
        {
            SetStatus("確認する音源がありません。", false);
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _preflightCancellation = cancellation;
        _preflightResult = null;
        _preflightResultsMenuItem.Enabled = false;
        _runningPreflightMenuItem = mode == SoundCheckMode.Read ? _preflightMenuItem : _decodeCheckMenuItem;
        var runningMenu = _runningPreflightMenuItem;
        var originalText = runningMenu.Text;
        runningMenu.Text = "音源の確認を中止";
        var otherMenu = mode == SoundCheckMode.Read ? _decodeCheckMenuItem : _preflightMenuItem;
        otherMenu.Enabled = false;
        var checkName = mode == SoundCheckMode.Read ? "先頭の読み取り" : "デコード";
        SetStatus($"{checkName}を確認中 0 / {paths.Length}", false);
        var progress = new Progress<int>(count =>
        {
            if (ReferenceEquals(_preflightCancellation, cancellation) && !cancellation.IsCancellationRequested)
            {
                SetStatus($"{checkName}を確認中 {count} / {paths.Length}", false);
            }
        });
        try
        {
            var result = await SoundPreflight.CheckAsync(paths, mode, progress, cancellation.Token,
                _audioEngine is AudioEngine engine ? engine.RunPreflightDecodeAsync : null);
            if (!cancellation.IsCancellationRequested)
            {
                _preflightResult = result;
                _preflightResultsMenuItem.Enabled = true;
                SetStatus($"{result.Summary} 詳細は「音源の確認結果」。", result.Failures.Count > 0);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!IsDisposed && !Disposing && !_cleanupCompleted && _statusLabel.Text == PreflightCancelling)
            {
                SetStatus("音源の確認を中止しました。", false);
            }
        }
        finally
        {
            _preflightCancellation = null;
            _runningPreflightMenuItem = null;
            if (!IsDisposed && !Disposing)
            {
                runningMenu.Text = originalText;
                runningMenu.Enabled = true;
                otherMenu.Enabled = true;
            }
        }
    }

    private void CancelSoundPreflight()
    {
        if (_preflightCancellation is not { IsCancellationRequested: false } cancellation) { return; }
        cancellation.Cancel();
        if (_runningPreflightMenuItem is { } menu)
        {
            menu.Text = "中止中（処理の終了待ち）";
            menu.Enabled = false;
        }
        if (!IsDisposed && !Disposing && !_cleanupCompleted) { SetStatus(PreflightCancelling, false); }
    }

    private void ShowSoundPreflightResult()
    {
        if (_preflightResult is not { } result) { return; }
        using var dialog = new Form
        {
            Text = result.Mode == SoundCheckMode.Read ? "先頭の読み取り結果" : "デコード確認結果",
            ClientSize = new Size(620, 340),
            AutoScaleMode = AutoScaleMode.Dpi,
            StartPosition = FormStartPosition.CenterParent
        };
        var summary = new Label { Text = result.Summary, Dock = DockStyle.Top, Height = 32, TextAlign = ContentAlignment.MiddleLeft };
        var details = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = string.Join(Environment.NewLine, result.Failures.Select(failure => $"{failure.Name} — {failure.Reason}"))
        };
        dialog.Controls.Add(details);
        dialog.Controls.Add(summary);
        UiTheme.Apply(dialog, _projectManager.Current.Settings.DarkMode);
        dialog.ShowDialog(this);
    }

    private Task<PlaybackStartResult> PlaySoundAsync(Guid soundId) =>
        PlaySoundFromAsync(soundId, "mouse", Stopwatch.GetTimestamp());

    private async Task<PlaybackStartResult> PlaySoundFromAsync(Guid soundId, string origin, long processedAt, bool exclusive = false)
    {
        CancelSoundPreflight();
        var detailed = _logger.DetailedLoggingEnabled;
        var timing = detailed || _audioEngine.PlaybackTimingEnabled ? new OperationTiming(processedAt) : null;
        try
        {
            return await PlaySoundCoreAsync(soundId, timing, processedAt, exclusive);
        }
        finally
        {
            if (timing is not null)
            {
                var line = $"play_timing sound={soundId} origin={origin} {timing.Format()}";
                if (detailed) { _logger.Debug(line); }
                else { _logger.Info(line); }
            }
        }
    }

    private async Task<PlaybackStartResult> PlaySoundCoreAsync(Guid soundId, OperationTiming? timing, long processedAt, bool exclusive)
    {
        void SetOutcome(string outcome)
        {
            if (timing is not null) { timing.Outcome = outcome; }
        }

        if (_diagnosticCancellation is not null || _systemSuspended || _manualEmergencyStopping
            || (_projectOperationInProgress && !_savingProject))
        {
            SetOutcome(_manualEmergencyStopping ? "emergency_stop" : "diagnostic_or_suspended");
            return PlaybackStartResult.Cancelled;
        }
        var project = _projectManager.Current;
        var sound = project.Sounds.FirstOrDefault(item => item.Id == soundId);
        if (sound is null)
        {
            SetOutcome("sound_removed");
            return PlaybackStartResult.Cancelled;
        }
        if ((exclusive || project.Settings.ExclusiveGroups) && !string.IsNullOrWhiteSpace(sound.Group))
        {
            var group = sound.Group.Trim();
            var active = _audioEngine.GetActiveSoundIds().ToHashSet();
            foreach (var peer in project.Sounds)
            {
                if (peer.Id != soundId && (active.Contains(peer.Id) || _pendingPlayback.ContainsKey(peer.Id))
                    && string.Equals(peer.Group.Trim(), group, StringComparison.OrdinalIgnoreCase))
                {
                    StopSound(peer.Id, immediately: exclusive);
                }
            }
        }
        var generation = Interlocked.Read(ref _playbackGeneration);
        var version = _uiPlayVersions.GetValueOrDefault(soundId) + 1;
        _uiPlayVersions[soundId] = version;
        bool IsCurrent() => !IsDisposed && !Disposing && generation == Interlocked.Read(ref _playbackGeneration)
            && ReferenceEquals(project, _projectManager.Current) && _uiPlayVersions.GetValueOrDefault(soundId) == version;
        SetPendingPlayback(soundId, PlaybackState.CheckingOutput);
        using (timing?.Measure("output_config"))
        {
            await _outputConfigurationTask;
            await _resumeOutputCheck;
        }
        using (timing?.Measure("output_probe"))
        {
            await CheckSelectedDeviceAvailability(force: true);
        }
        if (!IsCurrent())
        {
            SetOutcome(_outputUnavailable ? "output_unavailable" : "superseded_before_prepare");
            return PlaybackStartResult.Cancelled;
        }
        if (_outputUnavailable)
        {
            SetOutcome("output_unavailable");
            CompletePlaybackDisplay(soundId, PlaybackState.Failed);
            SetStatus("このPCで出力先を選び直してください。", true);
            return PlaybackStartResult.Failed;
        }
        var path = _projectManager.SnapshotPlaybackPath(sound);
        SetPendingPlayback(soundId, PlaybackState.Preparing);
        var result = PlaybackStartResult.Started;
        var binding = _outputBinding;
        var outputChanged = false;
        timing?.Record("to_engine", processedAt);
        if (_audioEngine is AudioEngine engine && binding is not null)
        {
            result = await engine.StartProjectPlaybackAsync(sound, path.File, path.Resolve, async token =>
            {
                if (!engine.NeedsOutputOpen || await ProbeBoundOutputAsync(binding, token)) { return; }
                outputChanged = true;
                throw new InvalidOperationException("再生準備中に出力先を確認できなくなりました。");
            }, timing);
            if (outputChanged)
            {
                MarkOutputChanged(binding);
                SetOutcome("output_changed_during_prepare");
            }
        }
        else
        {
            try
            {
                string resolved;
                using (timing?.Measure("resolve_path")) { resolved = await Task.Run(path.Resolve); }
                if (!IsCurrent())
                {
                    SetOutcome("superseded_before_prepare");
                    return PlaybackStartResult.Cancelled;
                }
                await _audioEngine.PlayAsync(sound, resolved);
                SetOutcome("started");
            }
            catch (Exception exception)
            {
                if (!IsCurrent()) { return PlaybackStartResult.Cancelled; }
                _logger.Error($"音源を再生できません: {sound.Name} ({sound.Id})", exception);
                SetStatus($"音源を再生できません: {sound.Name} — {exception.Message}", true);
                SetOutcome("failed");
                result = PlaybackStartResult.Failed;
            }
        }

        if (IsCurrent())
        {
            var missing = _projectManager.Current.MissingSoundIds;
            if ((result == PlaybackStartResult.Missing && missing.Add(soundId))
                || (result == PlaybackStartResult.Started && missing.Remove(soundId)))
            {
                UpdateProjectHeader();
            }
            CompletePlaybackDisplay(soundId, result switch
            {
                PlaybackStartResult.Missing => PlaybackState.Missing,
                PlaybackStartResult.Failed => PlaybackState.Failed,
                _ => _audioEngine.GetState(soundId)
            });
            return result;
        }
        if (timing?.Outcome == "started") { SetOutcome("superseded_after_commit"); }
        return PlaybackStartResult.Cancelled;
    }

    private void SetPendingPlayback(Guid soundId, PlaybackState state)
    {
        _pendingPlayback[soundId] = state;
        if (_soundCards.TryGetValue(soundId, out var card)) { card.UpdateState(state); }
    }

    private void CompletePlaybackDisplay(Guid soundId, PlaybackState state)
    {
        _pendingPlayback.Remove(soundId);
        if (_soundCards.TryGetValue(soundId, out var card)) { card.UpdateState(state); }
    }

    private void InvalidatePendingPlayback(bool readEngineState = true)
    {
        Interlocked.Increment(ref _playbackGeneration);
        foreach (var soundId in _pendingPlayback.Keys)
        {
            if (_soundCards.TryGetValue(soundId, out var card))
            {
                card.UpdateState(readEngineState ? _audioEngine.GetState(soundId) : PlaybackState.Stopped);
            }
        }
        _pendingPlayback.Clear();
    }

    private void PauseSound(Guid soundId)
    {
        if (_manualEmergencyStopping) { return; }
        // A pending decoder cannot be paused before it joins the mixer.
        // Cancel the pending request before pausing any already playing instance.
        if (_pendingPlayback.ContainsKey(soundId))
        {
            _uiPlayVersions[soundId] = _uiPlayVersions.GetValueOrDefault(soundId) + 1;
            if (_audioEngine is AudioEngine engine)
            {
                engine.CancelPendingPlayback(soundId);
                engine.Pause(soundId);
            }
            else { _audioEngine.StopImmediately(soundId); }
            CompletePlaybackDisplay(soundId, _audioEngine.GetState(soundId));
            return;
        }
        _audioEngine.Pause(soundId);
    }

    private void StopSound(Guid soundId, bool immediately = false)
    {
        if (_manualEmergencyStopping) { return; }
        _uiPlayVersions[soundId] = _uiPlayVersions.GetValueOrDefault(soundId) + 1;
        if (immediately) { _audioEngine.StopImmediately(soundId); }
        else { _audioEngine.Stop(soundId); }
        if (_pendingPlayback.ContainsKey(soundId)) { CompletePlaybackDisplay(soundId, _audioEngine.GetState(soundId)); }
    }

    private void StopAll(bool useConfiguredFade = true)
    {
        if (_manualEmergencyStopping) { return; }
        if (_diagnosticCancellation is not null)
        {
            StopForEmergency(invalidateConfiguration: false);
            return;
        }
        InvalidatePendingPlayback();
        var useFadeOut = useConfiguredFade && _projectManager.Current.Settings.FadeOutOnStopAll;
        _audioEngine.StopAll(useFadeOut);
        SetStatus(useFadeOut ? "全停止・フェード" : "全停止", false,
            useFadeOut ? "Fade Out設定のある音源はフェード後に停止します。" : "すべての音源を即時停止しました。");
    }

    private void ToggleMasterMute()
    {
        if (_manualEmergencyStopping) { return; }
        if (_editingLocked || _projectOperationInProgress) { CanEditProject(); return; }
        _masterMute.Checked = !_masterMute.Checked;
        SetStatus(_masterMute.Checked ? "ミュートしました。" : "ミュートを解除しました。", false);
    }

    private void EmergencyStopAll()
    {
        StopForEmergency(invalidateConfiguration: true);
    }

    private void QueueEmergencyStop()
    {
        if (_manualEmergencyStopping) { return; }
        _manualEmergencyStopping = true;
        _manualEmergencyStopTask = RunEmergencyStopAsync();
    }

    private async Task RunEmergencyStopAsync()
    {
        var requestedAt = Stopwatch.GetTimestamp();
        PrepareEmergencyStop(invalidateConfiguration: true, readEngineState: false);
        _resumeTriggersButton.Enabled = false;
        ApplyEditingLock();
        SetStatus("緊急停止中。音声出力を閉じています。", false);

        try
        {
            await (_audioEngine is AudioEngine engine ? engine.RequestEmergencyStopAsync() : Task.Run(_audioEngine.EmergencyStopAll));
        }
        catch (Exception exception)
        {
            _outputUnavailable = true;
            _outputNeedsReselection = true;
            _logger.Error("緊急停止中に音声出力を終了できませんでした。", exception);
        }
        finally
        {
            _manualEmergencyStopping = false;
            if (!IsDisposed && !Disposing)
            {
                _resumeTriggersButton.Enabled = _diagnosticCancellation is null;
                ApplyEditingLock();
                foreach (var (id, card) in _soundCards) { card.UpdateState(_audioEngine.GetState(id)); }
                ShowEmergencyStopResult();
            }
            if (_logger.DetailedLoggingEnabled)
            {
                _logger.Debug(string.Create(CultureInfo.InvariantCulture,
                    $"emergency_stop completed: elapsed_ms={Stopwatch.GetElapsedTime(requestedAt).TotalMilliseconds:F2}"));
            }
        }
    }

    private void PrepareEmergencyStop(bool invalidateConfiguration, bool readEngineState)
    {
        CancelSoundPreflight();
        if (invalidateConfiguration) { _outputConfigurationVersion++; }
        _diagnosticCancellation?.Cancel();
        if (_logger.DetailedLoggingEnabled) { _logger.Debug("automatic triggers suspended: emergency stop"); }
        _triggerEngine.Suspend();
        _resumeTriggersButton.Visible = true;
        InvalidatePendingPlayback(readEngineState);
    }

    private void StopForEmergency(bool invalidateConfiguration)
    {
        PrepareEmergencyStop(invalidateConfiguration, readEngineState: !_manualEmergencyStopping);
        if (_manualEmergencyStopping) { return; }
        // Keep held-key state so an auto-repeat does not count as a new press after the stop.
        try { _audioEngine.EmergencyStopAll(); }
        catch (Exception exception)
        {
            _outputUnavailable = true;
            _outputNeedsReselection = true;
            _logger.Error("緊急停止中に音声出力を終了できませんでした。", exception);
        }
        ShowEmergencyStopResult();
    }

    private void ShowEmergencyStopResult()
    {
        if (_audioEngine is AudioEngine { IsOutputFaulted: true })
        {
            _outputUnavailable = true;
            _outputNeedsReselection = true;
        }
        if (_outputUnavailable)
        {
            _deviceLabel.Text = "出力: エラー（設定で選び直す）";
            SetStatus("緊急停止・イベント休止中。設定で出力先を確認してください。", true);
            return;
        }
        SetStatus("緊急停止・イベント休止中", false, "手動再生は可能です。「イベント再開」で自動イベントを再開します。");
    }

    private void ExecuteShortcuts(IReadOnlyList<ShortcutCommand> commands) =>
        ExecuteShortcutsCore(commands, Stopwatch.GetTimestamp());

    private void ExecuteShortcutsCore(IReadOnlyList<ShortcutCommand> commands, long processedAt)
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
                    ExecuteShortcutsCore(commandSnapshot, processedAt);
                }
            });
            return;
        }

        if (_logger.DetailedLoggingEnabled)
        {
            foreach (var command in commands) { _logger.Debug($"shortcut dispatched: action={command.Type} sound={command.SoundId}"); }
        }
        var playIds = commands
            .Where(command => command.Type == ShortcutCommandType.Play && command.SoundId is not null)
            .Select(command => command.SoundId!.Value)
            .Distinct()
            .ToArray();
        if (playIds.Length > 1)
        {
            PlayShortcutBatch(playIds, processedAt);
            return;
        }

        foreach (var command in commands)
        {
            switch (command.Type)
            {
                case ShortcutCommandType.Play when command.SoundId is { } playId:
                    _ = PlaySoundFromAsync(playId, "shortcut", processedAt);
                    break;
                case ShortcutCommandType.PlayExclusive when command.SoundId is { } exclusiveId:
                    _ = PlaySoundFromAsync(exclusiveId, "shortcut", processedAt, exclusive: true);
                    break;
                case ShortcutCommandType.Stop when command.SoundId is { } stopId:
                    StopSound(stopId);
                    break;
                case ShortcutCommandType.StopAll:
                    StopAll();
                    break;
            }
        }
    }

    private void PlayShortcutBatch(IReadOnlyCollection<Guid> soundIds, long processedAt)
    {
        foreach (var soundId in soundIds)
        {
            _ = PlaySoundFromAsync(soundId, "shortcut", processedAt);
        }
    }

    private async Task ShowSettings()
    {
        if (_settingsDialogOpening || !CanEditProject()) { return; }
        _settingsDialogOpening = true;
        try { await ShowSettingsCoreAsync(); }
        finally { _settingsDialogOpening = false; }
    }

    private async Task ShowSettingsCoreAsync()
    {
        if (!CanEditProject()) { return; }
        var openingProject = _projectManager.Current;
        AudioDeviceSnapshot snapshot;
        try { snapshot = await CaptureOutputSnapshotAsync("settings_open"); }
        catch (Exception exception)
        {
            if (IsDisposed || Disposing) { return; }
            _logger.Error("出力デバイスの一覧を確認できません。", exception);
            if (_outputBinding is { } binding) { MarkOutputChanged(binding); }
            snapshot = new AudioDeviceSnapshot([], null);
            ShowOutputReselection("出力一覧を取得できません。接続を確認して設定画面で更新してください。");
        }
        if (IsDisposed || Disposing || !ReferenceEquals(openingProject, _projectManager.Current) || !CanEditProject()) { return; }
        if (_outputBinding is { } current && !current.Matches(snapshot)) { MarkOutputChanged(current); }
        var legacyCandidate = _outputPreference.FindLegacyCandidate(openingProject.Settings.OutputDevice, snapshot);
        using var dialog = new SettingsDialog(openingProject.Settings, snapshot.Devices,
            _outputUnavailable || snapshot.Devices.Count == 0, _outputPreference.SelectionId, legacyCandidate);
        dialog.ConfigureOutputRefresh(snapshot, async () =>
        {
            var refreshed = await CaptureOutputSnapshotAsync("settings_refresh");
            if (_outputBinding is { } active && !active.Matches(refreshed)) { MarkOutputChanged(active); }
            return refreshed;
        }, QueueEmergencyStop);
        dialog.SetProjectMetadata(_projectManager.Current.Project);
        UiTheme.Apply(dialog, _projectManager.Current.Settings.DarkMode);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        await _manualEmergencyStopTask;
        if (IsDisposed || Disposing || !ReferenceEquals(openingProject, _projectManager.Current) || !CanEditProject()) { return; }

        snapshot = dialog.OutputSnapshot ?? snapshot;
        var settings = _projectManager.Current.Settings;
        var selectedDevice = dialog.OutputSelectionExplicit
            ? snapshot.Devices.FirstOrDefault(device => device.DeviceNumber == dialog.SelectedDeviceNumber)
            : null;
        var savedOutput = true;
        if (dialog.OutputSelectionExplicit)
        {
            var selectedBinding = selectedDevice is null ? null : AudioOutputBinding.Create(selectedDevice.SelectionId, snapshot);
            if (selectedBinding is null)
            {
                SetStatus("選択した出力先を識別できません。接続を確認して設定を開き直してください。", true);
                return;
            }
            var result = await ConfirmOutputSelectionAsync(selectedBinding,
                legacyCandidate?.Device.SelectionId == selectedBinding.ConfiguredId);
            if (result is null || IsDisposed || Disposing || !ReferenceEquals(openingProject, _projectManager.Current)
                || !CanEditProject()) { return; }
            savedOutput = result.Value;
        }
        var project = _projectManager.Current.Project;
        var projectName = string.IsNullOrWhiteSpace(dialog.ProjectName)
            ? "名称未設定"
            : dialog.ProjectName;
        var changed = HasProjectSettingsChanges(
            settings,
            project,
            dialog.BackgroundInput,
            dialog.SilentLoop,
            dialog.FadeOutOnStop,
            dialog.FadeOutOnStopAll,
            dialog.DarkMode,
            dialog.SoundListColumns,
            projectName,
            dialog.ThemeColor,
            dialog.DetailedLogging) || settings.ExclusiveGroups != dialog.ExclusiveGroups;
        settings.BackgroundInput = dialog.BackgroundInput;
        settings.SilentLoop = dialog.SilentLoop;
        settings.FadeOutOnStop = dialog.FadeOutOnStop;
        settings.FadeOutOnStopAll = dialog.FadeOutOnStopAll;
        settings.ExclusiveGroups = dialog.ExclusiveGroups;
        settings.DarkMode = dialog.DarkMode;
        settings.DetailedLogging = dialog.DetailedLogging;
        settings.SoundListColumns = dialog.SoundListColumns;
        project.Name = projectName;
        project.ThemeColor = dialog.ThemeColor;
        if (changed)
        {
            _projectManager.MarkDirty(shortcutBindingsChanged: false);
        }

        ApplyProjectSettingsCore(allowOutputRebind: false);
        ResizeSoundCards();
        if (!savedOutput) { SetStatus("出力先を保存できませんでした。この起動中のみ有効です。", true); }
        else if (!_outputUnavailable) { SetStatus(changed || selectedDevice is not null ? "設定を更新" : "変更なし", false); }
    }

    internal static bool HasProjectSettingsChanges(
        ProjectSettings settings,
        ProjectMetadata project,
        bool backgroundInput,
        bool silentLoop,
        bool fadeOutOnStop,
        bool fadeOutOnStopAll,
        bool darkMode,
        int soundListColumns,
        string projectName,
        string themeColor,
        bool detailedLogging = false) =>
        settings.BackgroundInput != backgroundInput
        || settings.SilentLoop != silentLoop
        || settings.FadeOutOnStop != fadeOutOnStop
        || settings.FadeOutOnStopAll != fadeOutOnStopAll
        || settings.DarkMode != darkMode
        || settings.DetailedLogging != detailedLogging
        || settings.SoundListColumns != soundListColumns
        || !string.Equals(project.Name, projectName, StringComparison.Ordinal)
        || !string.Equals(project.ThemeColor, themeColor, StringComparison.Ordinal);

    private void ShowGroupShortcuts()
    {
        if (!CanEditProject()) { return; }
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
            SetStatus("グループキーに変更はありません。", false);
            return;
        }

        _projectManager.Current.GroupShortcuts = definitions;
        _projectManager.MarkDirty();
        SetStatus("グループキーを更新しました。", false);
    }

    internal static bool GroupShortcutsEqual(
        IReadOnlyList<GroupShortcutDefinition> first,
        IReadOnlyList<GroupShortcutDefinition> second) => first.Count == second.Count
        && first.Zip(second).All(pair =>
            string.Equals(pair.First.Group, pair.Second.Group, StringComparison.Ordinal)
            && string.Equals(pair.First.Key, pair.Second.Key, StringComparison.Ordinal));

    private void OnSoundGroupChangeCommitted(SoundCard card, GroupChangedEventArgs eventArgs)
    {
        if (_editingLocked) { return; }
        var previousGroup = eventArgs.PreviousGroup.Trim();
        var currentGroup = eventArgs.CurrentGroup.Trim();
        if (previousGroup.Length == 0
            || string.Equals(previousGroup, currentGroup, StringComparison.Ordinal)
            || _projectManager.Current.Sounds.Any(sound =>
                sound.Id != card.Sound.Id && string.Equals(sound.Group, previousGroup, StringComparison.OrdinalIgnoreCase)))
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
            HoldGroupTriggers(_projectManager.Current, previousGroup);
            RefreshTriggerData(resetEngine: false);
            SetStatus($"グループ「{previousGroup}」のイベントを保留しました。", true);
            return;
        }

        var destinationCount = _projectManager.Current.Sounds.Count(sound => sound.Id != card.Sound.Id
            && string.Equals(sound.Group, currentGroup, StringComparison.OrdinalIgnoreCase));
        if (destinationCount > 0)
        {
            var moveAll = new TaskDialogButton("関連設定も移す");
            var moveSound = new TaskDialogButton("音源だけ移す");
            var cancel = TaskDialogButton.Cancel;
            var result = TaskDialog.ShowDialog(this, new TaskDialogPage
            {
                Caption = "既存グループへの移動",
                Heading = $"「{currentGroup}」は{destinationCount + 1}音源になります。",
                Text = $"「{previousGroup}」の関連設定{referenceCount}件を移すと、操作対象が増えます。音源だけ移すと、元のグループのイベントを保留します。",
                Buttons = { moveAll, moveSound, cancel },
                DefaultButton = cancel
            });
            if (result == cancel)
            {
                eventArgs.Cancel = true;
                return;
            }
            if (result == moveSound)
            {
                HoldGroupTriggers(_projectManager.Current, previousGroup);
                RefreshTriggerData(resetEngine: false);
                SetStatus($"音源を移動し、グループ「{previousGroup}」のイベントを保留しました。", true);
                return;
            }
        }

        var updated = RetargetGroupReferences(_projectManager.Current, previousGroup, currentGroup);
        RefreshTriggerData(resetEngine: false);
        _projectManager.MarkDirty();
        SetStatus($"グループ名の変更に合わせて関連設定を{updated}件更新しました。", false);
    }

    private static void HoldGroupTriggers(ProjectModel model, string group)
    {
        foreach (var trigger in model.Triggers.Where(trigger =>
            string.Equals(trigger.TargetGroup, group, StringComparison.OrdinalIgnoreCase)))
        {
            trigger.TargetGroupOnHold = true;
            trigger.Enabled = false;
        }
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
                     !trigger.TargetGroupOnHold && string.Equals(trigger.TargetGroup, previousGroup, StringComparison.OrdinalIgnoreCase)))
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
        if (!CanEditProject() || _systemSuspended || _audioEngine is not AudioEngine engine) { return; }
        await _outputConfigurationTask;
        await _resumeOutputCheck;
        await CheckSelectedDeviceAvailability(force: true);
        if (IsDisposed || Disposing || _systemSuspended || !CanEditProject()) { return; }
        if (_outputUnavailable || _outputBinding is not { } binding)
        {
            SetStatus("診断する前に、設定で出力先を確認してください。", true);
            return;
        }

        if (MessageBox.Show(this,
            $"出力先: {binding.Device.Name}\nテスト音は最大10%で、マスター音量とミュートに従います。\n通常再生を停止し、イベントを休止します。診断を開始しますか？",
            "動作診断", MessageBoxButtons.OKCancel, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2) != DialogResult.OK) { return; }

        if (!_systemSuspended && !_outputUnavailable && ReferenceEquals(binding, _outputBinding))
        {
            await RunDiagnosticAsync(engine, binding);
        }
    }

    private async Task RunDiagnosticAsync(AudioEngine engine, AudioOutputBinding binding)
    {
        StopForEmergency(invalidateConfiguration: false);
        using var cancellation = new CancellationTokenSource();
        _diagnosticCancellation = cancellation;
        _cancelDiagnosticButton.Visible = true;
        _resumeTriggersButton.Enabled = false;
        ApplyEditingLock();
        SetStatus("動作診断を実行しています…", false);
        var outputLost = false;

        try
        {
            var result = await AudioSelfTest.RunAsync(engine, async token =>
            {
                try
                {
                    var snapshot = await _deviceManager.CaptureSnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2), token);
                    if (binding.Matches(snapshot) && engine.OutputDeviceNumber == binding.Device.DeviceNumber) { return; }
                }
                catch (Exception exception) when (!token.IsCancellationRequested)
                {
                    _logger.Error("診断中に出力先を再確認できませんでした。", exception);
                }

                outputLost = true;
                cancellation.Cancel();
                engine.EmergencyStopAll();
                token.ThrowIfCancellationRequested();
            }, _logger, cancellation.Token);
            var reportPath = AudioSelfTest.SaveReport(result, _logDirectory);
            SetStatus(result.Success ? "動作診断に合格しました。イベント休止中" : "動作診断で問題を検出しました。イベント休止中",
                !result.Success, $"{result.ToReport()}レポート: {reportPath}");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (outputLost && !IsDisposed && !Disposing)
            {
                _outputUnavailable = true;
                _deviceLabel.Text = "出力: エラー（設定で選び直す）";
                _logger.Warning("出力先を確認できなかったため動作診断を中止しました。");
                SetStatus("出力先を確認できなかったため診断を中止しました。設定で選び直してください。", true);
            }
        }
        catch (Exception exception)
        {
            await CheckSelectedDeviceAvailability(force: true);
            if (IsDisposed || Disposing) { return; }
            _logger.Error("動作診断を実行できませんでした。", exception);
            SetStatus($"動作診断を実行できませんでした: {exception.Message}", true);
        }
        finally
        {
            _diagnosticCancellation = null;
            if (!IsDisposed && !Disposing)
            {
                _cancelDiagnosticButton.Visible = false;
                _resumeTriggersButton.Enabled = true;
                ApplyEditingLock();
            }
        }
    }

    private void ApplyProjectSettings() => ApplyProjectSettingsCore(allowOutputRebind: true);

    private void ApplyProjectSettingsCore(bool allowOutputRebind)
    {
        var settings = _projectManager.Current.Settings;
        _logger.DetailedLoggingEnabled = settings.DetailedLogging;
        if (_logger.DetailedLoggingEnabled)
        {
            _logger.Debug($"settings applied: app={Application.ProductVersion} runtime={Environment.Version} os={Environment.OSVersion} dpi={DeviceDpi} "
                + $"sounds={_projectManager.Current.Sounds.Count} triggers={_projectManager.Current.Triggers.Count} "
                + $"output={_outputPreference.SelectionId ?? "unselected"} master_volume={settings.MasterVolume} mute={settings.MasterMute} "
                + $"silent_loop={settings.SilentLoop} fade_stop={settings.FadeOutOnStop} fade_all={settings.FadeOutOnStopAll} background_input={settings.BackgroundInput}");
        }
        _applyingProjectSettings = true;
        try
        {
            _masterVolume.Value = ProjectLimits.ToVolumePercent(settings.MasterVolume, 1);
            _masterMute.Checked = settings.MasterMute;
        }
        finally
        {
            _applyingProjectSettings = false;
        }

        _audioEngine.MasterVolume = settings.MasterVolume;
        _audioEngine.MasterMute = settings.MasterMute;
        _audioEngine.FadeOutOnStopEnabled = settings.FadeOutOnStop;

        if (allowOutputRebind)
        {
            _outputConfigurationTask = ConfigureOutputAsync(settings, ++_outputConfigurationVersion);
        }
        else if (!_outputUnavailable && !_systemSuspended
            && _audioEngine.SilentLoopEnabled != (settings.SilentLoop && !_triggerEngine.IsSuspended))
        {
            _outputConfigurationTask = ApplySilentLoopAsync(settings, _outputConfigurationVersion);
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
                _backgroundShortcutDispatcher.Reset();
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

    protected override void WndProc(ref Message m)
    {
        const int wmPowerBroadcast = 0x0218;
        const int pbtApmSuspend = 4;
        const int pbtApmResumeAutomatic = 18;
        const int pbtApmResumeSuspend = 7;
        const int wmTimeChange = 0x001E;
        if (_clockTimer is not null && !_cleanupCompleted)
        {
            if (m.Msg == wmPowerBroadcast && m.WParam.ToInt32() == pbtApmSuspend)
            {
                _systemSuspended = true;
                StopForClockDiscontinuity(verifyOutput: false);
                _previousClockTick = (DateTimeOffset.Now, Stopwatch.GetTimestamp());
            }
            else if (m.Msg == wmPowerBroadcast && (m.WParam.ToInt32() == pbtApmResumeAutomatic
                || (_systemSuspended && m.WParam.ToInt32() == pbtApmResumeSuspend)))
            {
                _systemSuspended = false;
                StopForClockDiscontinuity(verifyOutput: true);
            }
            else if (m.Msg == wmTimeChange && !_systemSuspended)
            {
                StopForClockDiscontinuity(verifyOutput: true);
            }
        }
        base.WndProc(ref m);
    }

    internal static bool ClockWasInterrupted(DateTimeOffset previous, DateTimeOffset now, TimeSpan monotonicElapsed)
    {
        return monotonicElapsed >= TimeSpan.FromSeconds(5) || WallClockChanged(previous, now, monotonicElapsed);
    }

    private static bool WallClockChanged(DateTimeOffset previous, DateTimeOffset now, TimeSpan monotonicElapsed) =>
        ((now - previous) - monotonicElapsed).Duration() >= TimeSpan.FromSeconds(2) || now.Offset != previous.Offset;

    private void StopForClockDiscontinuity(bool verifyOutput, bool uiStalled = false)
    {
        _previousClockTick = null;
        StopForEmergency(invalidateConfiguration: true);
        SetStatus(uiStalled ? "画面の処理が長時間止まったため、再生と自動イベントを停止しました。"
            : "スリープまたは時計の変更を検出しました。イベント休止中", false,
            "出力先を確認し、「イベント再開」を押してください。経過したイベントは実行されません。");
        if (verifyOutput) { _resumeOutputCheck = VerifyOutputAfterResumeAsync(_outputConfigurationVersion); }
    }

    private async Task VerifyOutputAfterResumeAsync(long version)
    {
        // A snapshot started before suspend cannot authorize playback after resume.
        var pendingCheck = _deviceCheckTask;
        await pendingCheck;
        if (!IsDisposed && !Disposing && !_systemSuspended && version == _outputConfigurationVersion)
        {
            await CheckSelectedDeviceAvailability(force: true);
        }
    }

    private void UpdateClockAndTriggers()
    {
        UpdateClockDisplay();
        if (!_loggingFailureShown && _logger is FileLogger { Diagnostics.IsDisabled: true }) { ReportLoggingUnavailable(); }
        var now = DateTimeOffset.Now;
        var timestamp = Stopwatch.GetTimestamp();
        if (_previousClockTick is { } previous
            && ClockWasInterrupted(previous.WallTime, now, Stopwatch.GetElapsedTime(previous.Timestamp, timestamp)))
        {
            _systemSuspended = false;
            StopForClockDiscontinuity(verifyOutput: true,
                uiStalled: !WallClockChanged(previous.WallTime, now, Stopwatch.GetElapsedTime(previous.Timestamp, timestamp)));
        }
        _previousClockTick = (now, timestamp);
        if (_systemSuspended || _manualEmergencyStopping) { return; }
        _ = CheckSelectedDeviceAvailability();
        if (_outputUnavailable || (_projectOperationInProgress && !_savingProject)) { return; }
        _triggerEngine.Tick(now, _stopwatch.Elapsed, _stopwatch.IsRunning);
    }

    private async Task ApplySilentLoopAsync(ProjectSettings settings, long version)
    {
        await CheckSelectedDeviceAvailability(force: true);
        if (IsDisposed || Disposing || _outputUnavailable || _systemSuspended || version != _outputConfigurationVersion) { return; }
        try { _audioEngine.SilentLoopEnabled = settings.SilentLoop && !_triggerEngine.IsSuspended; }
        catch (Exception exception)
        {
            OnOutputDeviceFallback(this, new OutputDeviceFallbackEventArgs(_audioEngine.OutputDeviceNumber, false, exception));
            _logger.Error("無音出力を変更できませんでした。", exception);
        }
    }

    internal async Task<bool?> ConfirmOutputSelectionAsync(AudioOutputBinding selection, bool legacyCandidate = false)
    {
        if (IsDisposed || Disposing || _systemSuspended || !CanEditProject()
            || !OutputDevicePreferences.IsValidId(selection.ConfiguredId)) { return null; }
        SetProjectOperationBusy(true);
        try
        {
            var version = ++_outputConfigurationVersion;
            var configuring = ConfigureOutputAsync(_projectManager.Current.Settings, version, selection);
            _outputConfigurationTask = configuring;
            if (!await configuring || IsDisposed || Disposing || _systemSuspended || version != _outputConfigurationVersion) { return null; }
            _outputPreference = new(selection.ConfiguredId, true);
            if (_outputPreferences is null) { _logger.Warning("このPCの出力設定の保存先を利用できません。"); }
            var saved = _outputPreferences is not null
                && await Task.Run(() => _outputPreferences.TrySave(selection.ConfiguredId, _logger));
            if (_logger.DetailedLoggingEnabled)
            {
                _logger.Debug($"output_selection source={(legacyCandidate ? "legacy_confirmation" : "user_selection")} persisted={saved}");
            }
            return saved;
        }
        finally { if (!IsDisposed && !Disposing) { SetProjectOperationBusy(false); } }
    }

    private async Task<bool> ConfigureOutputAsync(ProjectSettings settings, long version, AudioOutputBinding? expectedBinding = null)
    {
        _outputUnavailable = true;
        var configuredId = expectedBinding?.ConfiguredId ?? _outputPreference.SelectionId;
        if (expectedBinding is null && (_outputNeedsReselection || configuredId is null))
        {
            if (_logger.DetailedLoggingEnabled)
            {
                var reason = _outputNeedsReselection ? "confirmation_required" : _outputPreference.FileExists ? "invalid_local" : "no_local";
                _logger.Debug($"output_refused source=configure reason={reason}");
            }
            ShowOutputReselection("設定でこのPCの出力先を確定してください。");
            return false;
        }
        try
        {
            var snapshot = await CaptureOutputSnapshotAsync("configure");
            if (IsDisposed || Disposing || version != _outputConfigurationVersion
                || _systemSuspended || !ReferenceEquals(settings, _projectManager.Current.Settings)) { return false; }
            if (expectedBinding is not null && !expectedBinding.Matches(snapshot))
            {
                throw new InvalidOperationException("設定画面を開いた後に出力先が変化しました。設定で選び直してください。");
            }
            var binding = AudioOutputBinding.Create(configuredId!, snapshot)
                ?? throw new InvalidOperationException("出力先を安全に識別できません。設定で特定の出力デバイスを選び直してください。");
            if (_outputBinding is { } previous && (_outputNeedsReselection || !previous.Matches(snapshot)
                || !string.Equals(previous.ConfiguredId, binding.ConfiguredId, StringComparison.OrdinalIgnoreCase)))
            {
                if (expectedBinding is null)
                {
                    throw new InvalidOperationException("出力先の切断または変更を検出しました。");
                }
                StopForEmergency(invalidateConfiguration: false);
            }
            if (_triggerEngine.IsSuspended || !settings.SilentLoop) { _audioEngine.SilentLoopEnabled = false; }
            _audioEngine.SelectOutputDevice(binding.Device.DeviceNumber);
            _audioEngine.SilentLoopEnabled = settings.SilentLoop && !_triggerEngine.IsSuspended;
            if (_logger.DetailedLoggingEnabled)
            {
                _logger.Debug($"output_bound source={(expectedBinding is null ? "local" : "confirmed")} current={binding.Device.SelectionId} number={binding.Device.DeviceNumber}");
            }
            _outputBinding = binding;
            _outputUnavailable = false;
            _outputNeedsReselection = false;
            _deviceLabel.Text = string.Equals(configuredId, "default", StringComparison.OrdinalIgnoreCase)
                ? $"出力: 既定 → {binding.Device.Name}" : $"出力: {binding.Device.Name}";
            _headerToolTip.SetToolTip(_deviceLabel, _deviceLabel.Text);
            return true;
        }
        catch (Exception exception)
        {
            if (IsDisposed || Disposing || version != _outputConfigurationVersion) { return false; }
            _outputUnavailable = true;
            _outputNeedsReselection = true;
            EmergencyStopAll();
            _logger.Error("出力デバイスを使用できません。", exception);
            if (_logger.DetailedLoggingEnabled)
            {
                _logger.Debug($"output_refused source=configure reason={exception.GetType().Name}");
            }
            ShowOutputReselection(exception.Message);
            return false;
        }
    }

    private Task CheckSelectedDeviceAvailability(bool force = false)
    {
        if (!_deviceCheckTask.IsCompleted) { return _deviceCheckTask; }
        var now = DateTimeOffset.Now;
        if (_outputUnavailable || _outputBinding is not { } binding
            || (!force && now < _nextDeviceAvailabilityCheck)) { return Task.CompletedTask; }
        _nextDeviceAvailabilityCheck = now.AddSeconds(2);
        return _deviceCheckTask = CheckBoundOutputAsync(binding);
    }

    private async Task<AudioDeviceSnapshot> CaptureOutputSnapshotAsync(string source)
    {
        var snapshot = await _deviceManager.CaptureSnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2));
        if (_logger.DetailedLoggingEnabled
            && (_lastOutputSnapshot is null || AudioDeviceSnapshotChanges.Differ(_lastOutputSnapshot, snapshot)))
        {
            _logger.Debug($"output_snapshot source={source} {AudioDeviceSnapshotChanges.Describe(_lastOutputSnapshot, snapshot)}");
        }
        _lastOutputSnapshot = snapshot;
        return snapshot;
    }

    private async Task<bool> ProbeBoundOutputAsync(AudioOutputBinding binding, CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _deviceManager.CaptureSnapshotAsync()
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            if (binding.Matches(snapshot)) { return true; }
            if (_logger.DetailedLoggingEnabled)
            {
                _logger.Debug($"output_refused source=pre_start reason=binding_changed previous={binding.Device.SelectionId} {AudioDeviceSnapshotChanges.Describe(null, snapshot)}");
            }
            return false;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"output_refused source=pre_start reason={exception.GetType().Name}"); }
            return false;
        }
    }

    private async Task CheckBoundOutputAsync(AudioOutputBinding binding)
    {
        try
        {
            if (binding.Matches(await CaptureOutputSnapshotAsync("availability"))) { return; }
        }
        catch (Exception exception)
        {
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"output_refused source=availability reason={exception.GetType().Name}"); }
        }
        MarkOutputChanged(binding);
    }

    private void MarkOutputChanged(AudioOutputBinding binding)
    {
        if (IsDisposed || Disposing || !ReferenceEquals(binding, _outputBinding) || _outputUnavailable) { return; }
        _outputUnavailable = true;
        _outputNeedsReselection = true;
        EmergencyStopAll();
        _logger.Warning("出力デバイスまたはWindows既定出力の変更を検出し、安全停止しました。");
        if (_logger.DetailedLoggingEnabled)
        {
            _logger.Debug($"output_refused source=availability reason=binding_unavailable previous={binding.Device.SelectionId}");
        }
        ShowOutputReselection("出力先の切断または変更を検出しました。");
    }

    private void ShowOutputReselection(string detail)
    {
        const string guidance = "このPCで出力先を選び直してください。";
        _deviceLabel.Text = "出力: 要再選択";
        _headerToolTip.SetToolTip(_deviceLabel, $"{guidance} クリックで設定を開きます。");
        SetStatus($"{guidance} {detail}", true);
    }

    private void UpdateClockDisplay()
    {
        _stopwatchToggleButton.Text = _stopwatch.IsRunning ? "一時停止" : "開始";
        if (_stopwatchMode)
        {
            _clockModeLabel.Text = _stopwatch.IsRunning ? "SW 計測中" : "SW 停止";
            _clockLabel.Text = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            _clockLabel.ForeColor = _clockModeLabel.ForeColor;
        }
        else
        {
            _clockModeLabel.Text = "時計";
            _clockLabel.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            _clockLabel.ForeColor = _clockModeLabel.ForeColor;
        }
    }

    private void OnTriggerFired(object? sender, TriggerFiredEventArgs eventArgs)
    {
        var processedAt = Stopwatch.GetTimestamp();
        if (!eventArgs.IsCurrent || _triggerEngine.IsSuspended || (_projectOperationInProgress && !_savingProject)) { return; }
        var trigger = eventArgs.Trigger;
        if (trigger.TargetGroupOnHold) { return; }
        var targetIds = (string.IsNullOrWhiteSpace(trigger.TargetGroup)
            ? [trigger.TargetSoundId]
            : _projectManager.Current.Sounds
                .Where(sound => string.Equals(sound.Group, trigger.TargetGroup, StringComparison.OrdinalIgnoreCase))
                .Select(sound => sound.Id)
                .ToArray()).Where(id => id != Guid.Empty).ToArray();
        if (_logger.DetailedLoggingEnabled)
        {
            _logger.Debug($"trigger fired: id={trigger.Id} type={trigger.TriggerType} action={trigger.Action} targets={string.Join(",", targetIds)}");
        }
        if (targetIds.Length == 0)
        {
            SetStatus($"イベントの対象が見つかりません: {trigger.Memo}", true);
            return;
        }

        if (trigger.Action == TriggerAction.Play)
        {
            var cue = string.IsNullOrWhiteSpace(trigger.Memo) ? "再生イベント" : trigger.Memo;
            _ = TrackTriggerPlaybackAsync(cue, targetIds, processedAt);
            return;
        }

        foreach (var targetId in targetIds)
        {
            switch (trigger.Action)
            {
                case TriggerAction.Stop:
                    StopSound(targetId);
                    break;
                case TriggerAction.Pause:
                    PauseSound(targetId);
                    break;
            }
        }

        SetStatus($"イベント「{trigger.Memo}」: {trigger.Action switch
        {
            TriggerAction.Stop => "停止要求",
            _ => "一時停止要求"
        }} {targetIds.Length}件", false);
    }

    private async Task TrackTriggerPlaybackAsync(string cue, Guid[] targets, long processedAt)
    {
        var generation = Interlocked.Read(ref _playbackGeneration);
        SetStatus($"イベント「{cue}」: 再生要求 {targets.Length}件", false);
        var results = await Task.WhenAll(targets.Select(id => PlaySoundFromAsync(id, "trigger", processedAt)));
        if (IsDisposed || Disposing || generation != Interlocked.Read(ref _playbackGeneration)) { return; }
        var started = results.Count(result => result == PlaybackStartResult.Started);
        var failed = results.Count(result => result is PlaybackStartResult.Failed or PlaybackStartResult.Missing);
        var cancelled = results.Length - started - failed;
        var summary = $"イベント「{cue}」: {results.Length}件要求、{started}件開始、{failed}件失敗、{cancelled}件中止";
        _logger.Info(summary);
        SetStatus(summary, failed > 0);
    }

    private void OnSoundStateChanged(object? sender, SoundStateChangedEventArgs eventArgs)
    {
        if (eventArgs.State is PlaybackState.Paused or PlaybackState.Playing)
        {
            _triggerEngine.SetSoundPaused(eventArgs.SoundId, eventArgs.State == PlaybackState.Paused);
        }
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            var generation = Interlocked.Read(ref _playbackGeneration);
            TryBeginInvoke(() =>
            {
                if (generation == Interlocked.Read(ref _playbackGeneration)) { UpdateSoundStateDisplay(eventArgs); }
            });
            return;
        }

        UpdateSoundStateDisplay(eventArgs);
    }

    private void UpdateSoundStateDisplay(SoundStateChangedEventArgs eventArgs)
    {
        if (!_manualEmergencyStopping && !_pendingPlayback.ContainsKey(eventArgs.SoundId) && _soundCards.TryGetValue(eventArgs.SoundId, out var card))
        {
            card.UpdateState(_audioEngine.GetState(eventArgs.SoundId));
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

        if (!_outputUnavailable) { SetStatus($"{eventArgs.Message} {eventArgs.Exception.Message}", true); }
    }

    private void OnOutputDeviceFallback(object? sender, OutputDeviceFallbackEventArgs eventArgs)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            var generation = Interlocked.Read(ref _playbackGeneration);
            TryBeginInvoke(() =>
            {
                if (generation == Interlocked.Read(ref _playbackGeneration)) { OnOutputDeviceFallback(sender, eventArgs); }
            });
            return;
        }

        // Even a legacy/recovered notification must never authorize automatic rerouting.
        _outputUnavailable = true;
        _outputNeedsReselection = true;
        EmergencyStopAll();
        ShowOutputReselection("音声出力デバイス異常を検出しました。接続とWindowsの設定を確認してください。");
    }

    private void UpdateProjectHeader()
    {
        var missing = _projectManager.Current.MissingSoundIds.Count;
        _missingAudioMenuItem.Visible = missing > 0;
        _missingAudioMenuItem.Text = $"欠落音源 {missing}件…";
        var dirty = _projectManager.IsDirty ? " *" : string.Empty;
        _projectLabel.Text = $"{_projectManager.Current.Project.Name}{dirty}";
        var path = _projectManager.CurrentPath;
        var fileName = path is null ? "未保存" : Path.GetFileName(path);
        var lockState = _editingLocked ? " [編集ロック]" : string.Empty;
        Text = $"pondex — {_projectManager.Current.Project.Name} [{fileName}]{dirty}{lockState}";
        _headerToolTip.SetToolTip(_projectLabel, path ?? "まだ保存されていないプロジェクトです。");
        _stopAllButton.Text = _projectManager.Current.Settings.FadeOutOnStopAll
            ? "■ 全停止\nフェード" : "■ 全停止\n即時";
    }

    private void RefreshRecentMenu()
    {
        _recentMenu.DropDownItems.Clear();
        var recent = _recentProjects.Load(includeUnavailable: true);
        if (recent.Count == 0)
        {
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem("（なし）") { Enabled = false });
            return;
        }

        foreach (var path in recent)
        {
            var available = File.Exists(path);
            var item = new ToolStripMenuItem(available ? path : $"{path}（未接続）") { Enabled = available };
            item.Click += async (_, _) => await OpenProjectAsync(path);
            _recentMenu.DropDownItems.Add(item);
        }
    }

    private async Task<bool> EnsureCanReplaceProjectAsync()
    {
        if (_projectOperationInProgress || _diagnosticCancellation is not null)
        {
            SetStatus("プロジェクト操作または診断が完了するまで待ってください。", true);
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
            "pondex",
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
        var externalPathCount = model.ApprovedSoundPaths.Values.Count(path =>
            ProjectPath.ClassifyResolved(projectPath, path) == ProjectPathKind.ExternalLocal);
        var enabledTriggerCount = model.Triggers.Count(trigger => trigger.Enabled);
        if (externalPathCount == 0 && !model.Settings.BackgroundInput && enabledTriggerCount == 0 && !model.Settings.DetailedLogging)
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

        if (model.Settings.DetailedLogging)
        {
            risks.Add($"・詳細ログ: このPCの音源のパスなどを {_logDirectory} に記録します");
        }

        var message = "このプロジェクトには、確認が必要な設定があります。\n"
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
            MainMenuStrip.Enabled = !busy || _savingProject;
        }

        _tabs.Enabled = !busy || _savingProject;
        foreach (var command in _saveCommands) { command.Enabled = !busy; }
        ApplyEditingLock();
        UseWaitCursor = busy;
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_manualEmergencyStopping)
        {
            eventArgs.Cancel = true;
            SetStatus("緊急停止中。音声出力を閉じています。", false);
            return;
        }
        if (_projectOperationInProgress)
        {
            eventArgs.Cancel = true;
            return;
        }

        if (_diagnosticCancellation is not null) { StopForEmergency(invalidateConfiguration: false); }
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
        CancelSoundPreflight();
        if (_diagnosticCancellation is not null)
        {
            _diagnosticCancellation.Cancel();
            _audioEngine.EmergencyStopAll();
        }
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
        CancelSoundPreflight();
        var version = _projectManager.ShortcutBindingsVersion;
        if (_shortcutBindingsVersion != version)
        {
            _shortcutBindingsVersion = version;
            _shortcutDispatcher.RefreshBindings();
            _backgroundShortcutDispatcher.RefreshBindings();
        }
        UpdateProjectHeader();
    }

    private void OnPlaybackStarted(object? sender, SoundPlaybackEventArgs eventArgs) =>
        _triggerEngine.OnSoundStarted(eventArgs.SoundId);

    private void OnPlaybackEnded(object? sender, SoundPlaybackEventArgs eventArgs) =>
        _triggerEngine.OnSoundEnded(
            eventArgs.SoundId,
            eventArgs.EndReason == PlaybackEndReason.Natural);

    private bool IsOwnedWindow(Form form)
    {
        for (Form? window = form; window is not null; window = window.Owner)
        {
            if (window == this) { return true; }
        }
        return false;
    }

    private bool HandleWindowShortcut(Keys key, bool down, bool repeat)
    {
        if (key == (Keys.Control | Keys.Shift | Keys.R))
        {
            if (down && !repeat) { QueueEmergencyStop(); }
            return true;
        }
        return (!down || !_headerGrip.Focused || key is not (Keys.Up or Keys.Down or Keys.Home))
            && _shortcutDispatcher.OnKeyChanged(key, down, repeat);
    }

    private void OnGlobalKeyChanged(object? sender, GlobalKeyEventArgs eventArgs)
    {
        if (eventArgs.HandledByWindowThread && eventArgs.IsDown) { return; }
        var generation = eventArgs.Generation ?? Interlocked.Read(ref _playbackGeneration);
        TryBeginInvoke(() =>
        {
            if (_projectManager.Current.Settings.BackgroundInput
                && (!eventArgs.IsDown || generation == Interlocked.Read(ref _playbackGeneration)))
            {
                _backgroundShortcutDispatcher.OnKeyChanged(eventArgs.KeyData, eventArgs.IsDown, eventArgs.IsRepeat);
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
        if (_editingLocked || _projectOperationInProgress || _diagnosticCancellation is not null)
        {
            eventArgs.Effect = DragDropEffects.None;
            return;
        }

        if (eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            eventArgs.Effect = DragDropEffects.Copy;
        }
    }

    private async void OnDragDrop(object? sender, DragEventArgs eventArgs)
    {
        if (_editingLocked || _projectOperationInProgress || _diagnosticCancellation is not null)
        {
            return;
        }

        if (eventArgs.Data?.GetData(DataFormats.FileDrop) is string[] paths)
        {
            if (paths.Length == 1 && Path.GetExtension(paths[0]).ToLowerInvariant() is "" or ".pondex" or ".xml")
            {
                await OpenProjectAsync(paths[0]);
                return;
            }
            AddSoundFiles(paths);
        }
    }


    private bool IsEditingText()
    {
        if ((_projectOperationInProgress && !_savingProject) || !Enabled || OwnedForms.Any(form => form.Visible && form.Modal)
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

    private void ShowLogLocation()
    {
        if (_uiPreferences is null) { return; }
        var nextLocal = _uiPreferences.UseLocalLogs;
        var actualPath = _logger is FileLogger { CurrentLogPath: null } or FileLogger { Diagnostics.IsDisabled: true }
            ? $"記録できません（保存先: {_logDirectory}）" : _logDirectory;
        var next = nextLocal ? "ローカル（このPC）" : "ポータブル（通常は実行フォルダ）";
        var change = new TaskDialogButton(nextLocal ? "ポータブルに変更" : "このPCに変更");
        var cancel = TaskDialogButton.Cancel;
        var answer = TaskDialog.ShowDialog(this, new TaskDialogPage
        {
            Caption = "ログの保存先",
            Heading = $"次回起動時の保存先　{next}",
            Text = $"現在のログ保存先\n{actualPath}\n\n変更は次回起動から有効です。このPCを選ぶと、共有PCでもログがそのPCに残ります。",
            Buttons = { change, cancel },
            DefaultButton = cancel
        });
        if (answer != change) { return; }

        try
        {
            _uiPreferences.SaveUseLocalLogs(!nextLocal);
            SetStatus("ログ保存先の変更は次回起動時に適用されます。", false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error("ログ保存先の設定を保存できません。", exception);
            SetStatus("ログ保存先の設定を保存できませんでした。", true);
        }
    }

    internal void ReportLocalLogFallback()
    {
        var detail = $"ローカルログを開始できません。現在の保存先: {_logDirectory}";
        if (_loggingFailureShown) { detail = "ローカルログも通常のログ保存先も利用できません。"; }
        SetStatus(detail, true);
    }

    private void ShowHelp()
    {
        using var dialog = new HelpDialog(QueueEmergencyStop);
        UiTheme.Apply(dialog, _projectManager.Current.Settings.DarkMode);
        dialog.ShowDialog(this);
    }

    private void ApplyDisplayTheme(bool darkMode)
    {
        _darkMode = darkMode;
        _headerGrip.BackColor = darkMode ? Color.FromArgb(39, 43, 47) : SystemColors.ControlLight;
        _headerGrip.ForeColor = darkMode ? Color.Silver : SystemColors.ControlDarkDark;
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
        SetStatus(statusText, _statusIsError, _statusLabel.ToolTipText);
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

    internal void ReportLoggingUnavailable()
    {
        if (_loggingFailureShown) { return; }
        _loggingFailureShown = true;
        SetStatus("ログを記録できません。保存フォルダを確認してください。", true);
    }

    private void SetStatus(string message, bool error, string? detail = null)
    {
        _statusIsError = error;
        _statusLabel.Text = message;
        _statusLabel.ToolTipText = detail ?? message;
        _statusLabel.AccessibleDescription = detail ?? message;
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
