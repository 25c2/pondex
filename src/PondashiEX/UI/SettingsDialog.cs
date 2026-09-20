using PondashiEX.Audio;
using PondashiEX.Project;

namespace PondashiEX.UI;

public sealed class SettingsDialog : Form
{
    private readonly ComboBox _deviceCombo;
    private readonly CheckBox _backgroundInputCheck;
    private readonly CheckBox _silentLoopCheck;
    private readonly CheckBox _fadeOutOnStopCheck;
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
        ClientSize = new Size(560, 388);
        MinimumSize = new Size(440, 300);
        DpiWindowHelper.Attach(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 8,
            AutoScroll = true
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
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _projectNameText = new TextBox { Text = string.Empty, Dock = DockStyle.Fill };
        _themeColorText = new TextBox { Text = "#202020", Width = 100 };

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
            Text = "他のアプリが前面でもキーアサインを受け付ける",
            Checked = settings.BackgroundInput,
            AutoSize = true
        };
        _silentLoopCheck = new CheckBox
        {
            Text = "無音ストリームを維持して頭切れを防ぐ",
            Checked = settings.SilentLoop,
            AutoSize = true
        };
        _fadeOutOnStopCheck = new CheckBox
        {
            Text = "通常の停止操作でFade Outを使う",
            Checked = settings.FadeOutOnStop,
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
        root.Controls.Add(new Label { Text = "テーマ色 (#RRGGBB)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        root.Controls.Add(_themeColorText, 1, 1);
        root.Controls.Add(new Label { Text = "出力デバイス", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        root.Controls.Add(_deviceCombo, 1, 2);
        root.Controls.Add(new Label { Text = "バックグラウンド入力", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        root.Controls.Add(_backgroundInputCheck, 1, 3);
        root.Controls.Add(new Label { Text = "無音ループ", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
        root.Controls.Add(_silentLoopCheck, 1, 4);
        root.Controls.Add(new Label { Text = "停止時フェード", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
        root.Controls.Add(_fadeOutOnStopCheck, 1, 5);
        root.Controls.Add(new Label { Text = "音源一覧の列数 (1～8)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
        root.Controls.Add(_soundListColumns, 1, 6);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Height = 32 };
        var cancelButton = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Width = 90, Height = 32 };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        root.Controls.Add(buttons, 0, 7);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public int SelectedDeviceNumber => _deviceCombo.SelectedValue is int number ? number : -1;

    public bool BackgroundInput => _backgroundInputCheck.Checked;

    public bool SilentLoop => _silentLoopCheck.Checked;

    public bool FadeOutOnStop => _fadeOutOnStopCheck.Checked;

    public int SoundListColumns => (int)_soundListColumns.Value;

    public string ProjectName => _projectNameText.Text.Trim();

    public string ThemeColor => _themeColorText.Text.Trim();

    public void SetProjectMetadata(ProjectMetadata metadata)
    {
        _projectNameText.Text = metadata.Name;
        _themeColorText.Text = metadata.ThemeColor;
    }
}
