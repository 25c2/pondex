using PondashiEX.Audio;
using PondashiEX.Project;

namespace PondashiEX.UI;

public sealed class SoundCard : UserControl
{
    private readonly Label _stateLabel;
    private readonly Label _nameLabel;
    private readonly Label _summaryLabel;
    private readonly Button _pauseButton;
    private readonly TrackBar _volumeTrack;
    private readonly Panel _detailsPanel;
    private readonly TextBox _keyText;
    private readonly TextBox _groupText;
    private readonly TextBox _nameText;
    private readonly TextBox _memoText;
    private readonly NumericUpDown _panValue;
    private readonly NumericUpDown _pitchValue;
    private readonly CheckBox _loopCheck;
    private readonly NumericUpDown _loopStart;
    private readonly CheckBox _fadeInCheck;
    private readonly NumericUpDown _fadeInValue;
    private readonly CheckBox _fadeOutCheck;
    private readonly NumericUpDown _fadeOutValue;
    private PlaybackState _state;
    private bool _suppressVolumeChanged;

    public SoundCard(SoundDefinition sound)
    {
        Sound = sound;
        AutoScaleMode = AutoScaleMode.Dpi;
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Color.FromArgb(245, 245, 245);
        Margin = new Padding(6);
        MinimumSize = new Size(620, 126);
        Height = 126;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 3,
            Padding = new Padding(8)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
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
        var detailsButton = CreateButton("詳細 ▼", SystemColors.Control);

        _volumeTrack = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Value = (int)(Math.Clamp(sound.Volume, 0, 1) * 100),
            Dock = DockStyle.Fill
        };
        var volumePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        volumePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        volumePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        volumePanel.Controls.Add(new Label { Text = "音量", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter }, 0, 0);
        volumePanel.Controls.Add(_volumeTrack, 1, 0);

        root.Controls.Add(_nameLabel, 0, 0);
        root.SetColumnSpan(_nameLabel, 2);
        root.Controls.Add(_summaryLabel, 2, 0);
        root.SetColumnSpan(_summaryLabel, 3);
        root.Controls.Add(_stateLabel, 5, 0);
        root.Controls.Add(playButton, 1, 1);
        root.Controls.Add(stopButton, 2, 1);
        root.Controls.Add(_pauseButton, 3, 1);
        root.Controls.Add(volumePanel, 4, 1);
        root.Controls.Add(detailsButton, 5, 1);

        _detailsPanel = new Panel { Dock = DockStyle.Fill, Height = 142, Visible = false };
        var details = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(4)
        };
        _nameText = new TextBox { Text = sound.Name, Width = 170 };
        _keyText = new TextBox { Text = sound.Key, Width = 52, MaxLength = 20 };
        _groupText = new TextBox { Text = sound.Group, Width = 100 };
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
            Maximum = long.MaxValue,
            Value = Math.Max(0, sound.Loop.StartSample),
            Width = 120
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
        _memoText = new TextBox { Text = sound.Memo, Width = 230 };
        AddLabeled(details, "名前", _nameText);
        AddLabeled(details, "キー", _keyText);
        AddLabeled(details, "グループ", _groupText);
        AddLabeled(details, "Pan", _panValue);
        AddLabeled(details, "Pitch", _pitchValue);
        details.Controls.Add(_loopCheck);
        AddLabeled(details, "開始sample", _loopStart);
        details.Controls.Add(_fadeInCheck);
        AddLabeled(details, "秒", _fadeInValue);
        details.Controls.Add(_fadeOutCheck);
        AddLabeled(details, "秒", _fadeOutValue);
        AddLabeled(details, "メモ", _memoText);
        var fileText = new TextBox
        {
            Text = sound.File,
            ReadOnly = true,
            Width = 330,
            TabStop = false
        };
        AddLabeled(details, "ファイル", fileText);
        _detailsPanel.Controls.Add(details);
        root.Controls.Add(_detailsPanel, 0, 2);
        root.SetColumnSpan(_detailsPanel, 6);

        Controls.Add(root);

        playButton.Click += (_, _) => PlayRequested?.Invoke(this, EventArgs.Empty);
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
        detailsButton.Click += (_, _) =>
        {
            _detailsPanel.Visible = !_detailsPanel.Visible;
            Height = _detailsPanel.Visible ? 276 : 126;
            detailsButton.Text = _detailsPanel.Visible ? "詳細 ▲" : "詳細 ▼";
        };
        _volumeTrack.ValueChanged += (_, _) =>
        {
            if (_suppressVolumeChanged)
            {
                return;
            }

            Sound.Volume = _volumeTrack.Value / 100.0f;
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
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
            var seconds = _fadeOutCheck.Checked ? (double)_fadeOutValue.Value : 0;
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
        _nameText.TextChanged += (_, _) => UpdateDefinition();
        _keyText.TextChanged += (_, _) => UpdateDefinition();
        _groupText.TextChanged += (_, _) => UpdateDefinition();
        _panValue.ValueChanged += (_, _) => UpdateDefinition();
        _pitchValue.ValueChanged += (_, _) => UpdateDefinition();
        _loopCheck.CheckedChanged += (_, _) => UpdateDefinition();
        _loopStart.ValueChanged += (_, _) => UpdateDefinition();
        _fadeInCheck.CheckedChanged += (_, _) => UpdateDefinition();
        _fadeInValue.ValueChanged += (_, _) => UpdateDefinition();
        _fadeOutCheck.CheckedChanged += (_, _) => UpdateDefinition();
        _fadeOutValue.ValueChanged += (_, _) => UpdateDefinition();
        _memoText.TextChanged += (_, _) => UpdateDefinition();

        UpdateState(PlaybackState.Stopped);
    }

    public SoundDefinition Sound { get; }

    public event EventHandler? PlayRequested;

    public event EventHandler? StopRequested;

    public event EventHandler? PauseRequested;

    public event EventHandler? ResumeRequested;

    public event EventHandler? DefinitionChanged;

    public event EventHandler<VolumeFadeRequestedEventArgs>? VolumeFadeRequested;

    public void UpdateState(PlaybackState state)
    {
        _state = state;
        (_stateLabel.Text, _stateLabel.BackColor) = state switch
        {
            PlaybackState.Playing => ("再生中", Color.LightGreen),
            PlaybackState.Paused => ("一時停止", Color.Khaki),
            PlaybackState.Fading => ("フェード中", Color.LightSkyBlue),
            _ => ("停止中", SystemColors.Control)
        };
        _pauseButton.Text = state == PlaybackState.Paused ? "▶ 再開" : "Ⅱ 一時停止";
    }

    private static Button CreateButton(string text, Color color) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        BackColor = color,
        UseVisualStyleBackColor = false,
        Margin = new Padding(4),
        Font = new Font(SystemFonts.DefaultFont.FontFamily, 10, FontStyle.Bold)
    };

    private static void AddLabeled(FlowLayoutPanel panel, string text, Control control)
    {
        panel.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(8, 7, 2, 0)
        });
        panel.Controls.Add(control);
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

    private void UpdateDefinition()
    {
        Sound.Name = string.IsNullOrWhiteSpace(_nameText.Text) ? "名称未設定" : _nameText.Text.Trim();
        Sound.Key = _keyText.Text.Trim();
        Sound.Group = _groupText.Text.Trim();
        Sound.Pan = (float)_panValue.Value / 100.0f;
        Sound.Pitch = (float)_pitchValue.Value;
        Sound.Loop.Enabled = _loopCheck.Checked;
        Sound.Loop.StartSample = (long)_loopStart.Value;
        Sound.FadeIn.Enabled = _fadeInCheck.Checked;
        Sound.FadeIn.Value = (float)_fadeInValue.Value;
        Sound.FadeOut.Enabled = _fadeOutCheck.Checked;
        Sound.FadeOut.Value = (float)_fadeOutValue.Value;
        Sound.Memo = _memoText.Text;
        _nameLabel.Text = Sound.Name;
        _summaryLabel.Text = SummaryText();
        DefinitionChanged?.Invoke(this, EventArgs.Empty);
    }

    private string SummaryText() => $"キー: {DisplayOrDash(Sound.Key)}   グループ: {DisplayOrDash(Sound.Group)}";

    private static string DisplayOrDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
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
