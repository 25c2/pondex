using System.Globalization;
using PondashiEX.Audio;
using PondashiEX.Input;
using PondashiEX.Project;

namespace PondashiEX.UI;

public sealed class SoundCard : UserControl
{
    private const int CollapsedHeight = 144;
    private readonly Label _stateLabel;
    private readonly Label _nameLabel;
    private readonly Label _summaryLabel;
    private readonly Button _pauseButton;
    private readonly Button _detailsButton;
    private readonly TrackBar _volumeTrack;
    private readonly NumericUpDown _volumeValue;
    private readonly Panel _detailsPanel;
    private readonly FlowLayoutPanel _detailsContent;
    private TextBox _keyText = null!;
    private TextBox _groupText = null!;
    private TextBox _nameText = null!;
    private TextBox _memoText = null!;
    private TextBox _fileText = null!;
    private NumericUpDown _panValue = null!;
    private NumericUpDown _pitchValue = null!;
    private CheckBox _loopCheck = null!;
    private NumericUpDown _loopStart = null!;
    private CheckBox _fadeInCheck = null!;
    private NumericUpDown _fadeInValue = null!;
    private CheckBox _fadeOutCheck = null!;
    private NumericUpDown _fadeOutValue = null!;
    private readonly Dictionary<Control, object> _editSnapshots = [];
    private readonly ToolTip _toolTip = new();
    private PlaybackState _state;
    private bool _suppressVolumeChanged;
    private bool _normalizingKey;
    private bool _loopSecondsEdited;
    private bool _darkMode;
    private string _committedGroup;
    private bool _updatingPlaybackSettings;
    private bool _detailsCreated;
    private bool _updatingHeight;
    private readonly Func<string> _resolveSoundPath;
    private string _loopDisplayText = string.Empty;

    internal bool DetailsCreated => _detailsCreated;

    public SoundCard(SoundDefinition sound, Func<string>? resolveSoundPath = null)
    {
        Sound = sound;
        _resolveSoundPath = resolveSoundPath ?? (() => Sound.File);
        _committedGroup = sound.Group;
        sound.FadeIn.Value = NormalizeFadeValue(sound.FadeIn);
        sound.FadeOut.Value = NormalizeFadeValue(sound.FadeOut);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Color.FromArgb(245, 245, 245);
        Tag = UiTheme.ElevatedSurfaceRole;
        Margin = new Padding(6);
        MinimumSize = new Size(320, CollapsedHeight);
        Height = CollapsedHeight;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4,
            Padding = new Padding(8)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _nameLabel = new Label
        {
            Text = sound.Name,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _stateLabel = new Label
        {
            Text = "停止中",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle
        };
        _summaryLabel = new Label
        {
            Text = SummaryText(),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        var playButton = CreateButton("▶ 再生", Color.FromArgb(215, 245, 215));
        var stopButton = CreateButton("■ 停止", Color.FromArgb(255, 225, 225));
        _pauseButton = CreateButton("Ⅱ 一時停止", Color.FromArgb(255, 245, 205));
        _detailsButton = CreateButton("詳細 ▼", SystemColors.Control);
        playButton.Tag = UiTheme.SuccessButtonRole;
        stopButton.Tag = UiTheme.StopButtonRole;
        _pauseButton.Tag = UiTheme.WarningButtonRole;

        _volumeTrack = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            TickStyle = TickStyle.None,
            Value = (int)(Math.Clamp(sound.Volume, 0, 1) * 100),
            Dock = DockStyle.Fill
        };
        _volumeValue = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100,
            DecimalPlaces = 0,
            Increment = 1,
            Value = _volumeTrack.Value,
            Dock = DockStyle.Fill,
            TextAlign = HorizontalAlignment.Right,
            Margin = new Padding(3, 5, 0, 5)
        };
        var volumePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        volumePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        volumePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        volumePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        volumePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
        volumePanel.Controls.Add(new Label { Text = "音量", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter }, 0, 0);
        volumePanel.Controls.Add(_volumeTrack, 1, 0);
        volumePanel.Controls.Add(_volumeValue, 2, 0);
        volumePanel.Controls.Add(new Label { Text = "%", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 3, 0);

        root.Controls.Add(_nameLabel, 0, 0);
        root.SetColumnSpan(_nameLabel, 2);
        root.Controls.Add(_summaryLabel, 2, 0);
        root.Controls.Add(_stateLabel, 3, 0);
        root.Controls.Add(playButton, 0, 1);
        root.Controls.Add(stopButton, 1, 1);
        root.Controls.Add(_pauseButton, 2, 1);
        root.Controls.Add(_detailsButton, 3, 1);
        root.Controls.Add(volumePanel, 0, 2);
        root.SetColumnSpan(volumePanel, 4);

        _detailsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Visible = false
        };
        _detailsContent = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(4)
        };
        _detailsPanel.Controls.Add(_detailsContent);
        root.Controls.Add(_detailsPanel, 0, 3);
        root.SetColumnSpan(_detailsPanel, 4);

        Controls.Add(root);

        playButton.Click += (_, _) =>
        {
            CommitLoopStartInput();
            PlayRequested?.Invoke(this, EventArgs.Empty);
        };
        stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        _pauseButton.Click += (_, _) =>
        {
            if (_state == PlaybackState.Paused)
            {
                ResumeRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                PauseRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        _detailsButton.Click += (_, _) =>
        {
            EnsureDetailsCreated();
            _detailsPanel.Visible = !_detailsPanel.Visible;
            _detailsButton.Text = _detailsPanel.Visible ? "詳細 ▲" : "詳細 ▼";
            UpdatePreferredHeight();
        };
        SizeChanged += (_, _) =>
        {
            UpdateState(_state);
            if (_detailsPanel.Visible)
            {
                UpdatePreferredHeight();
            }
        };
        _volumeTrack.ValueChanged += (_, _) =>
        {
            if (_volumeValue.Value != _volumeTrack.Value)
            {
                _volumeValue.Value = _volumeTrack.Value;
            }

            if (_suppressVolumeChanged)
            {
                return;
            }

            Sound.Volume = _volumeTrack.Value / 100.0f;
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        };
        _volumeValue.ValueChanged += (_, _) =>
        {
            var value = decimal.ToInt32(_volumeValue.Value);
            if (_volumeTrack.Value != value)
            {
                _volumeTrack.Value = value;
            }
        };
        _volumeTrack.MouseUp += (_, eventArgs) =>
        {
            if (eventArgs.Button != MouseButtons.Right)
            {
                return;
            }

            var availableWidth = Math.Max(1, _volumeTrack.ClientSize.Width - 1);
            var targetPercent = Math.Clamp(
                (int)Math.Round(eventArgs.X * 100.0 / availableWidth),
                0,
                100);
            _suppressVolumeChanged = true;
            try
            {
                _volumeTrack.Value = targetPercent;
            }
            finally
            {
                _suppressVolumeChanged = false;
            }

            Sound.Volume = targetPercent / 100.0f;
            var seconds = Sound.FadeOut.Enabled ? Sound.FadeOut.Value : 0;
            if (seconds > 0)
            {
                VolumeFadeRequested?.Invoke(
                    this,
                    new VolumeFadeRequestedEventArgs(Sound.Volume, TimeSpan.FromSeconds(seconds)));
            }
            else
            {
                DefinitionChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        RegisterEditor(_volumeValue);
        RegisterBackgroundFocusTargets(this);

        _toolTip.SetToolTip(_volumeTrack, "0～100%。ホイール、矢印キー、ドラッグで調整。右クリックで音量フェードを設定できます。");
        _pauseButton.SizeChanged += (_, _) => UpdateState(_state);
        UpdateState(PlaybackState.Stopped);
    }

    internal void EnsureDetailsCreated()
    {
        if (_detailsCreated) { return; }
        var sound = Sound;
        _detailsContent.SuspendLayout();
        try
        {
            _nameText = new TextBox { Text = sound.Name, Width = 170, MaxLength = ProjectLimits.MaxNameLength };
            _keyText = new TextBox { Text = sound.Key, Width = 52, MaxLength = 20 };
            _groupText = new TextBox { Text = sound.Group, Width = 100, MaxLength = ProjectLimits.MaxGroupLength };
            _panValue = new NumericUpDown
            {
                Minimum = -100,
                Maximum = 100,
                DecimalPlaces = 0,
                Value = (decimal)Math.Clamp(sound.Pan * 100, -100, 100),
                Width = 64
            };
            _loopCheck = new CheckBox { Text = "ループ", Checked = sound.Loop.Enabled, AutoSize = true };
            _loopStart = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 24 * 60 * 60,
                DecimalPlaces = 2,
                Increment = 0.10m,
                Value = (decimal)Math.Clamp(
                    GetLoopStartSecondsForDisplay(),
                    0,
                    24 * 60 * 60),
                Width = 92
            };
            _pitchValue = new NumericUpDown
            {
                Minimum = 0.50m,
                Maximum = 2.00m,
                DecimalPlaces = 2,
                Increment = 0.05m,
                Value = (decimal)Math.Clamp(sound.Pitch, 0.5f, 2.0f),
                Width = 68
            };
            _fadeInCheck = new CheckBox { Text = "Fade In", Checked = sound.FadeIn.Enabled, AutoSize = true };
            _fadeInValue = CreateFadeValue(sound.FadeIn.Value);
            _fadeOutCheck = new CheckBox { Text = "Fade Out", Checked = sound.FadeOut.Enabled, AutoSize = true };
            _fadeOutValue = CreateFadeValue(sound.FadeOut.Value);
            _memoText = new TextBox { Text = sound.Memo, Width = 230, MaxLength = ProjectLimits.MaxMemoLength };
            AddLabeled(_detailsContent, "名前", _nameText);
            AddLabeled(_detailsContent, "キー", _keyText);
            AddLabeled(_detailsContent, "グループ", _groupText);
            AddLabeled(_detailsContent, "Pan", _panValue);
            AddLabeled(_detailsContent, "Pitch", _pitchValue);
            AddToggleValue(_detailsContent, _loopCheck, "開始（秒）", _loopStart);
            AddToggleValue(_detailsContent, _fadeInCheck, "秒", _fadeInValue);
            AddToggleValue(_detailsContent, _fadeOutCheck, "秒", _fadeOutValue);
            AddLabeled(_detailsContent, "メモ", _memoText);
            _fileText = new TextBox
            {
                Text = sound.File,
                ReadOnly = true,
                Width = 280,
                TabStop = false
            };
            var filePanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = Padding.Empty
            };
            var browseButton = new Button { Text = "参照…", Width = 76, Height = 28, Margin = new Padding(4, 0, 0, 0) };
            var deleteButton = new Button
            {
                Text = "この音源を削除…",
                AutoSize = true,
                Height = 28,
                BackColor = Color.FromArgb(255, 225, 225),
                UseVisualStyleBackColor = false,
                Margin = new Padding(8, 2, 4, 2)
            };
            deleteButton.Tag = UiTheme.StopButtonRole;
            filePanel.Controls.Add(_fileText);
            filePanel.Controls.Add(browseButton);
            AddLabeled(_detailsContent, "ファイル", filePanel);
            _detailsContent.Controls.Add(deleteButton);
            var playbackSettingsButton = new Button { Text = "速度・音程の調整…", AutoSize = true, Height = 28 };
            playbackSettingsButton.Click += (_, _) => ShowPlaybackSettings();
            _detailsContent.Controls.Add(playbackSettingsButton);
            _toolTip.SetToolTip(playbackSettingsButton, "再生を止めずに速度・音程・Panを変更します。");
            _toolTip.SetToolTip(_keyText, "空白は使用できません。A、2、F1などのキー名を入力します。");
            _toolTip.SetToolTip(_panValue, "-100で左、0で中央、+100で右。右クリックで中央へ戻せます。");
            _toolTip.SetToolTip(_loopStart, "初回は0秒から再生し、音源末尾に達したときに戻る位置です。音源長以上なら先頭へ戻ります。");
            _toolTip.SetToolTip(_fadeInCheck, "再生開始時に、無音から設定音量まで指定秒数で上げます。");
            _toolTip.SetToolTip(_fadeOutCheck, "停止時に、指定秒数で音量を下げてから停止します。");
            _toolTip.SetToolTip(deleteButton, "音声ファイル自体は削除せず、このプロジェクトから登録だけを削除します。");

            browseButton.Click += (_, _) => FileBrowseRequested?.Invoke(this, EventArgs.Empty);
            deleteButton.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
            _nameText.TextChanged += (_, _) => UpdateDefinition();
            _keyText.TextChanged += (_, _) => NormalizeKeyAndUpdate();
            _keyText.Validated += (_, _) => CommitKeyInput();
            _groupText.TextChanged += (_, _) => UpdateDefinition();
            _groupText.Validated += (_, _) => CommitGroupChange();
            _panValue.ValueChanged += (_, _) => UpdateDefinition();
            _panValue.MouseDown += (_, eventArgs) =>
            {
                if (eventArgs.Button == MouseButtons.Right)
                {
                    _panValue.Value = 0;
                }
            };
            _pitchValue.ValueChanged += (_, _) => UpdateDefinition();
            _loopCheck.CheckedChanged += (_, _) => UpdateDefinition();
            _loopStart.ValueChanged += (_, _) =>
            {
                if (_updatingPlaybackSettings) { return; }
                _loopSecondsEdited = true;
                UpdateDefinition();
                _loopDisplayText = _loopStart.Text;
            };
            _loopStart.Validated += (_, _) => CommitLoopStartInput();
            _fadeInCheck.CheckedChanged += (_, _) =>
            {
                SetDefaultFadeSecondsWhenEnabled(_fadeInCheck, _fadeInValue);
                UpdateDefinition();
            };
            _fadeInValue.ValueChanged += (_, _) => UpdateDefinition();
            _fadeOutCheck.CheckedChanged += (_, _) =>
            {
                SetDefaultFadeSecondsWhenEnabled(_fadeOutCheck, _fadeOutValue);
                UpdateDefinition();
            };
            _fadeOutValue.ValueChanged += (_, _) => UpdateDefinition();
            _memoText.TextChanged += (_, _) => UpdateDefinition();

            RegisterEditor(_nameText);
            RegisterEditor(_keyText);
            RegisterEditor(_groupText);
            RegisterEditor(_panValue);
            RegisterEditor(_pitchValue);
            RegisterEditor(_loopStart);
            RegisterEditor(_fadeInValue);
            RegisterEditor(_fadeOutValue);
            RegisterEditor(_memoText);

            RegisterBackgroundFocusTargets(_detailsContent);
            _loopDisplayText = _loopStart.Text;
            _detailsCreated = true;
            UiTheme.Apply(_detailsPanel, _darkMode);
        }
        finally
        {
            _detailsContent.ResumeLayout(true);
        }
    }

    public SoundDefinition Sound { get; }

    public event EventHandler? PlayRequested;

    public event EventHandler? StopRequested;

    public event EventHandler? PauseRequested;

    public event EventHandler? ResumeRequested;

    public event EventHandler? DefinitionChanged;

    public event EventHandler? FileBrowseRequested;

    public event EventHandler? DeleteRequested;

    public event EventHandler? InvalidKeyRejected;

    public event EventHandler<VolumeFadeRequestedEventArgs>? VolumeFadeRequested;


    public event EventHandler<GroupChangedEventArgs>? GroupChangeCommitted;

    internal void ApplyDarkMode(bool darkMode)
    {
        _darkMode = darkMode;
        UiTheme.Apply(this, darkMode);
        UpdateState(_state);
    }

    public void UpdateState(PlaybackState state)
    {
        _state = state;
        (_stateLabel.Text, _stateLabel.BackColor) = state switch
        {
            PlaybackState.Playing => ("再生中", _darkMode ? Color.FromArgb(42, 91, 59) : Color.LightGreen),
            PlaybackState.Paused => ("一時停止", _darkMode ? Color.FromArgb(105, 86, 38) : Color.Khaki),
            PlaybackState.Fading => ("フェード中", _darkMode ? Color.FromArgb(45, 78, 105) : Color.LightSkyBlue),
            _ => ("停止中", _darkMode ? Color.FromArgb(58, 62, 67) : SystemColors.Control)
        };
        _stateLabel.ForeColor = _darkMode ? Color.FromArgb(238, 240, 242) : SystemColors.ControlText;
        var pauseTextWidth = TextRenderer.MeasureText("Ⅱ 一時停止", _pauseButton.Font).Width
            + _pauseButton.Padding.Horizontal + 10 * DeviceDpi / 96;
        _pauseButton.Text = state == PlaybackState.Paused ? "▶ 再開"
            : (_pauseButton.ClientSize.Width < pauseTextWidth ? "Ⅱ 休止" : "Ⅱ 一時停止");
        _toolTip.SetToolTip(_pauseButton, state == PlaybackState.Paused ? "再生を再開" : "一時停止");
    }

    internal void EndEditing() => _detailsButton.Focus();

    internal void UpdateFilePath(string path)
    {
        if (_detailsCreated)
        {
            _fileText.Text = path;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Button CreateButton(string text, Color color) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        BackColor = color,
        UseVisualStyleBackColor = false,
        Margin = new Padding(4),
        Font = UiTheme.CreateFont(10F, FontStyle.Bold)
    };

    private static void AddLabeled(FlowLayoutPanel panel, string text, Control control)
    {
        var field = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(4, 2, 4, 2)
        };
        field.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(4, 7, 2, 0)
        });
        field.Controls.Add(control);
        panel.Controls.Add(field);
    }

    private static void AddToggleValue(
        FlowLayoutPanel panel,
        CheckBox toggle,
        string valueLabel,
        Control valueControl)
    {
        var field = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(4, 2, 4, 2)
        };
        toggle.Margin = new Padding(4, 5, 4, 0);
        field.Controls.Add(toggle);
        field.Controls.Add(new Label
        {
            Text = valueLabel,
            AutoSize = true,
            Margin = new Padding(2, 7, 2, 0)
        });
        field.Controls.Add(valueControl);
        panel.Controls.Add(field);
    }

    private static NumericUpDown CreateFadeValue(float value) => new()
    {
        Minimum = 0,
        Maximum = 60,
        DecimalPlaces = 2,
        Increment = 0.10m,
        Value = (decimal)Math.Clamp(value, 0, 60),
        Width = 70
    };

    private static float NormalizeFadeValue(FadeSettings settings) =>
        settings.Enabled && settings.Value <= 0 ? 1.0f : Math.Clamp(settings.Value, 0, 60);

    private static void SetDefaultFadeSecondsWhenEnabled(CheckBox toggle, NumericUpDown value)
    {
        if (toggle.Checked && value.Value <= 0)
        {
            value.Value = 1.00m;
        }
    }

    private void RegisterEditor(Control editor)
    {
        editor.Enter += (_, _) => _editSnapshots[editor] = GetEditorValue(editor);
        editor.KeyDown += OnEditorKeyDown;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (sender is not Control editor || eventArgs.KeyCode is not (Keys.Enter or Keys.Escape))
        {
            return;
        }

        if (eventArgs.KeyCode == Keys.Escape && _editSnapshots.TryGetValue(editor, out var value))
        {
            SetEditorValue(editor, value);
            UpdateDefinition();
        }

        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        _detailsButton.Focus();
    }

    private void RegisterBackgroundFocusTargets(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is not TextBoxBase and not NumericUpDown and not ComboBox
                and not Button and not CheckBox and not TrackBar)
            {
                child.MouseDown += (_, _) => _detailsButton.Focus();
            }

            RegisterBackgroundFocusTargets(child);
        }
    }

    private static object GetEditorValue(Control editor) => editor switch
    {
        TextBoxBase textBox => textBox.Text,
        NumericUpDown numeric => numeric.Value,
        _ => throw new InvalidOperationException($"未対応の編集コントロールです: {editor.GetType().Name}")
    };

    private static void SetEditorValue(Control editor, object value)
    {
        switch (editor)
        {
            case TextBoxBase textBox:
                textBox.Text = (string)value;
                break;
            case NumericUpDown numeric:
                numeric.Value = (decimal)value;
                break;
            default:
                throw new InvalidOperationException($"未対応の編集コントロールです: {editor.GetType().Name}");
        }
    }

    private void UpdatePreferredHeight()
    {
        if (_updatingHeight) { return; }
        _updatingHeight = true;
        try
        {
            if (!_detailsPanel.Visible)
            {
                if (Height != CollapsedHeight)
                {
                    Height = CollapsedHeight;
                }

                return;
            }

            var availableWidth = Math.Max(1, ClientSize.Width - 24);
            // File + browse must fit as a single field even in a three-column card.
            var fileField = _fileText.Parent!.Parent!;
            var otherWidth = fileField.GetPreferredSize(Size.Empty).Width - _fileText.Width;
            _fileText.Width = Math.Max(40, availableWidth - _detailsContent.Padding.Horizontal
                - fileField.Margin.Horizontal - otherWidth);
            _detailsContent.MaximumSize = new Size(availableWidth, 0);
            _detailsContent.Width = availableWidth;
            _detailsContent.PerformLayout();
            var detailsHeight = _detailsContent.Controls.Cast<Control>()
                .Select(control => control.Bottom + control.Margin.Bottom).DefaultIfEmpty(0).Max()
                + _detailsContent.Padding.Bottom;
            var targetHeight = CollapsedHeight + Math.Max(36, detailsHeight) + 12;
            if (Height != targetHeight)
            {
                Height = targetHeight;
            }
        }
        finally { _updatingHeight = false; }
    }

    private void UpdateDefinition()
    {
        if (!_detailsCreated || _updatingPlaybackSettings)
        {
            return;
        }

        Sound.Name = string.IsNullOrWhiteSpace(_nameText.Text) ? "名称未設定" : _nameText.Text.Trim();
        Sound.Key = _keyText.Text.Trim();
        Sound.Group = _groupText.Text.Trim();
        Sound.Pan = (float)_panValue.Value / 100.0f;
        Sound.Pitch = (float)_pitchValue.Value;
        Sound.Loop.Enabled = _loopCheck.Checked;
        if (_loopSecondsEdited)
        {
            Sound.Loop.StartSeconds = (double)_loopStart.Value;
            Sound.Loop.StartSample = 0;
        }
        Sound.FadeIn.Enabled = _fadeInCheck.Checked;
        Sound.FadeIn.Value = (float)_fadeInValue.Value;
        Sound.FadeOut.Enabled = _fadeOutCheck.Checked;
        Sound.FadeOut.Value = (float)_fadeOutValue.Value;
        Sound.Memo = _memoText.Text;
        _nameLabel.Text = Sound.Name;
        _summaryLabel.Text = SummaryText();
        DefinitionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowPlaybackSettings()
    {
        using var dialog = new PlaybackSettingsDialog(Sound);
        UiTheme.Apply(dialog, _darkMode);
        dialog.SettingsChanged += (_, _) =>
        {
            _updatingPlaybackSettings = true;
            try
            {
                Sound.Speed = dialog.Speed;
                _pitchValue.Value = (decimal)dialog.Pitch;
                _panValue.Value = (decimal)(dialog.Pan * 100);
            }
            finally
            {
                _updatingPlaybackSettings = false;
            }

            UpdateDefinition();
        };
        dialog.ShowDialog(this);
    }

    private void NormalizeKeyAndUpdate()
    {
        if (_normalizingKey)
        {
            return;
        }

        var normalized = new string(_keyText.Text.Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (!string.Equals(normalized, _keyText.Text, StringComparison.Ordinal))
        {
            _normalizingKey = true;
            try
            {
                var selectionStart = Math.Min(normalized.Length, _keyText.SelectionStart);
                _keyText.Text = normalized;
                _keyText.SelectionStart = selectionStart;
            }
            finally
            {
                _normalizingKey = false;
            }
        }

        UpdateDefinition();
    }

    private void CommitKeyInput()
    {
        if (string.IsNullOrWhiteSpace(_keyText.Text))
        {
            return;
        }

        var valid = ShortcutKey.TryNormalize(_keyText.Text, out var normalized);
        _normalizingKey = true;
        try
        {
            _keyText.Text = valid ? normalized : string.Empty;
            _keyText.SelectionStart = _keyText.TextLength;
        }
        finally
        {
            _normalizingKey = false;
        }

        UpdateDefinition();
        if (!valid)
        {
            InvalidKeyRejected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CommitLoopStartInput()
    {
        if (!_detailsCreated || string.Equals(_loopStart.Text, _loopDisplayText, StringComparison.Ordinal))
        {
            return;
        }

        if (decimal.TryParse(
                _loopStart.Text,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var typedValue))
        {
            var safeValue = Math.Clamp(typedValue, _loopStart.Minimum, _loopStart.Maximum);
            if (_loopStart.Value != safeValue)
            {
                _loopStart.Value = safeValue;
            }
            else
            {
                _loopSecondsEdited = true;
                UpdateDefinition();
            }
            _loopDisplayText = _loopStart.Text;
        }

        // Merely playing an existing legacy project must not rewrite sample positions using rounded UI values.
    }

    private void CommitGroupChange()
    {
        var currentGroup = Sound.Group;
        if (string.Equals(_committedGroup, currentGroup, StringComparison.Ordinal))
        {
            return;
        }

        var previousGroup = _committedGroup;
        _committedGroup = currentGroup;
        GroupChangeCommitted?.Invoke(this, new GroupChangedEventArgs(previousGroup, currentGroup));
    }

    private double GetLoopStartSecondsForDisplay()
    {
        if (Sound.Loop.StartSeconds > 0 || Sound.Loop.StartSample <= 0)
        {
            return Sound.Loop.StartSeconds;
        }
        try
        {
            using var decoder = AudioDecoderFactory.Open(_resolveSoundPath());
            return Sound.Loop.StartSample / (double)decoder.WaveFormat.SampleRate;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or FormatException or InvalidOperationException
            or System.Runtime.InteropServices.ExternalException)
        {
            _detailsContent.Controls.Add(new Label
            {
                Text = $"旧開始位置: {Sound.Loop.StartSample} sample（音源を参照できず秒換算不可。編集するまで元の値を保持）",
                AutoSize = true
            });
            return 0;
        }
    }

    private string SummaryText() => $"キー: {DisplayOrDash(Sound.Key)}   グループ: {DisplayOrDash(Sound.Group)}";

    private static string DisplayOrDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}

public sealed class GroupChangedEventArgs : EventArgs
{
    public GroupChangedEventArgs(string previousGroup, string currentGroup)
    {
        PreviousGroup = previousGroup;
        CurrentGroup = currentGroup;
    }

    public string PreviousGroup { get; }

    public string CurrentGroup { get; }
}

public sealed class VolumeFadeRequestedEventArgs : EventArgs
{
    public VolumeFadeRequestedEventArgs(float targetVolume, TimeSpan duration)
    {
        TargetVolume = targetVolume;
        Duration = duration;
    }

    public float TargetVolume { get; }

    public TimeSpan Duration { get; }
}
