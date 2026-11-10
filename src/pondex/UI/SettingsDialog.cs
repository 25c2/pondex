using pondex.Audio;
using pondex.Project;

namespace pondex.UI;

public sealed class SettingsDialog : Form
{
    private readonly ToolTip _toolTip = new();
    private readonly ComboBox _deviceCombo;
    private readonly CheckBox _backgroundInputCheck;
    private readonly CheckBox _silentLoopCheck;
    private readonly CheckBox _fadeOutOnStopCheck;
    private readonly CheckBox _fadeOutOnStopAllCheck;
    private readonly CheckBox _darkModeCheck;
    private readonly CheckBox _detailedLoggingCheck;
    private readonly TextBox _projectNameText;
    private readonly TextBox _themeColorText;
    private readonly NumericUpDown _soundListColumns;

    public SettingsDialog(ProjectSettings settings, IReadOnlyList<AudioDeviceInfo> devices)
    {
        Text = "設定";
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 514);
        MinimumSize = new Size(440, 300);
        DpiWindowHelper.Attach(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 10
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _projectNameText = new TextBox { Text = string.Empty, Dock = DockStyle.Fill, MaxLength = ProjectLimits.MaxNameLength };
        _themeColorText = new TextBox { Text = "#202020", Width = 100, MaxLength = ProjectLimits.MaxThemeColorLength };

        _deviceCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(AudioDeviceInfo.Name),
            ValueMember = nameof(AudioDeviceInfo.DeviceNumber),
            Dock = DockStyle.Fill,
            DataSource = devices.ToList()
        };
        var selectedDevice = AudioDeviceManager.FindConfiguredDevice(devices, settings.OutputDevice)
            ?? AudioDeviceInfo.Default;
        _deviceCombo.SelectedValue = selectedDevice.DeviceNumber;
        if (_deviceCombo.SelectedIndex < 0)
        {
            _deviceCombo.SelectedValue = -1;
        }

        _backgroundInputCheck = new CheckBox
        {
            Text = "他のアプリでも有効",
            Checked = settings.BackgroundInput,
            AutoSize = true
        };
        _silentLoopCheck = new CheckBox
        {
            Text = "無音出力を維持",
            Checked = settings.SilentLoop,
            AutoSize = true
        };
        _fadeOutOnStopCheck = new CheckBox
        {
            Text = "音源ごとの設定を使う",
            Checked = settings.FadeOutOnStop,
            AutoSize = true
        };
        _fadeOutOnStopAllCheck = new CheckBox
        {
            Text = "音源ごとの設定を使う",
            Checked = settings.FadeOutOnStopAll,
            AutoSize = true
        };
        _darkModeCheck = new CheckBox
        {
            Text = "ダークモード",
            Checked = settings.DarkMode,
            AutoSize = true
        };
        _detailedLoggingCheck = new CheckBox
        {
            Text = "詳細ログ",
            Checked = settings.DetailedLogging,
            AutoSize = true
        };
        _soundListColumns = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 8,
            Value = Math.Clamp(settings.SoundListColumns, 1, 8),
            Width = 72
        };

        root.Controls.Add(new Label { Text = "プロジェクト名", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        root.Controls.Add(_projectNameText, 1, 0);
        root.Controls.Add(new Label { Text = "テーマ色", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        root.Controls.Add(_themeColorText, 1, 1);
        root.Controls.Add(new Label { Text = "出力デバイス", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        root.Controls.Add(_deviceCombo, 1, 2);
        root.Controls.Add(new Label { Text = "キー入力", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        root.Controls.Add(_backgroundInputCheck, 1, 3);
        root.Controls.Add(new Label { Text = "出力保持", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
        root.Controls.Add(_silentLoopCheck, 1, 4);
        root.Controls.Add(new Label { Text = "停止フェード", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
        root.Controls.Add(_fadeOutOnStopCheck, 1, 5);
        root.Controls.Add(new Label { Text = "全停止フェード", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
        root.Controls.Add(_fadeOutOnStopAllCheck, 1, 6);
        root.Controls.Add(new Label { Text = "表示列数", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 7);
        root.Controls.Add(_soundListColumns, 1, 7);
        root.Controls.Add(new Label { Text = "配色", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 8);
        root.Controls.Add(_darkModeCheck, 1, 8);
        root.Controls.Add(new Label { Text = "診断", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 9);
        root.Controls.Add(_detailedLoggingCheck, 1, 9);

        _toolTip.SetToolTip(_themeColorText, "色を#RRGGBB形式で指定します。例: #202020");
        _toolTip.SetToolTip(_backgroundInputCheck, "別のアプリを操作中も、音源の割り当てキーを受け付けます。");
        _toolTip.SetToolTip(_silentLoopCheck, "出力ストリームを維持し、再生の頭切れを防ぎます。");
        _toolTip.SetToolTip(_fadeOutOnStopCheck, "通常の停止時に、その音源のFade Out設定を使います。");
        _toolTip.SetToolTip(_fadeOutOnStopAllCheck, "全停止時に、それぞれの音源のFade Out設定を使います。緊急停止は常に即時停止です。");
        _toolTip.SetToolTip(_detailedLoggingCheck, "再生要求・準備時間・停止理由などを記録します。音源パスを含むため、共有前に内容を確認してください。");
        _backgroundInputCheck.AccessibleDescription = "他のアプリが前面でも音源の割り当てキーを受け付ける";
        _silentLoopCheck.AccessibleDescription = "無音出力を維持して頭切れを防ぐ";
        Disposed += (_, _) => _toolTip.Dispose();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            WrapContents = false,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft
        };
        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(116, 36) };
        var cancelButton = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(116, 36) };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(root);
        Controls.Add(scroll);
        Controls.Add(buttons);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public int SelectedDeviceNumber => _deviceCombo.SelectedValue is int number ? number : -1;

    public bool BackgroundInput => _backgroundInputCheck.Checked;

    public bool SilentLoop => _silentLoopCheck.Checked;

    public bool FadeOutOnStop => _fadeOutOnStopCheck.Checked;

    public bool FadeOutOnStopAll => _fadeOutOnStopAllCheck.Checked;

    public bool DarkMode => _darkModeCheck.Checked;

    public bool DetailedLogging => _detailedLoggingCheck.Checked;

    public int SoundListColumns => (int)_soundListColumns.Value;

    public string ProjectName => _projectNameText.Text.Trim();

    public string ThemeColor => _themeColorText.Text.Trim();

    public void SetProjectMetadata(ProjectMetadata metadata)
    {
        _projectNameText.Text = metadata.Name;
        _themeColorText.Text = metadata.ThemeColor;
    }
}
