using pondex.Audio;
using pondex.Project;

namespace pondex.UI;

public sealed class SettingsDialog : Form
{
    private readonly ToolTip _toolTip = new();
    private readonly ComboBox _deviceCombo;
    private readonly Button _refreshDevices;
    private readonly Button _okButton;
    private readonly Button _emergencyStop;
    private readonly Label _outputLabel;
    private bool _requireOutputSelection;
    private bool _refreshingDevices;
    private bool _outputRefreshFailed;
    private bool _showingLegacyCandidate;
    private readonly CheckBox _backgroundInputCheck;
    private readonly CheckBox _silentLoopCheck;
    private readonly CheckBox _fadeOutOnStopCheck;
    private readonly CheckBox _fadeOutOnStopAllCheck;
    private readonly CheckBox _exclusiveGroupsCheck;
    private readonly CheckBox _darkModeCheck;
    private readonly CheckBox _detailedLoggingCheck;
    private readonly TextBox _projectNameText;
    private readonly TextBox _themeColorText;
    private readonly NumericUpDown _soundListColumns;

    public SettingsDialog(ProjectSettings settings, IReadOnlyList<AudioDeviceInfo> devices, bool requireOutputSelection = false)
        : this(settings, devices, requireOutputSelection, AudioDeviceInfo.Default.PersistentId, null)
    {
    }

    internal SettingsDialog(ProjectSettings settings, IReadOnlyList<AudioDeviceInfo> devices, bool requireOutputSelection,
        string? outputId, AudioOutputBinding? legacyCandidate)
    {
        Text = "設定";
        _requireOutputSelection = requireOutputSelection;
        _showingLegacyCandidate = legacyCandidate is not null;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 550);
        MinimumSize = new Size(440, 300);
        DpiWindowHelper.Attach(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 11
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
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _projectNameText = new TextBox { Text = string.Empty, Dock = DockStyle.Fill, MaxLength = ProjectLimits.MaxNameLength };
        _themeColorText = new TextBox { Text = "#202020", Width = 100, MaxLength = ProjectLimits.MaxThemeColorLength };

        _deviceCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(DeviceChoice.DisplayName),
            ValueMember = nameof(DeviceChoice.DeviceNumber),
            Dock = DockStyle.Fill,
            DataSource = GetDeviceChoices(devices)
        };
        var selectedDevice = legacyCandidate?.Device
            ?? (!requireOutputSelection && outputId is not null ? AudioDeviceManager.FindConfiguredDevice(devices, outputId) : null);
        void SelectInitialOutput()
        {
            if (selectedDevice is null) { _deviceCombo.SelectedIndex = -1; }
            else { _deviceCombo.SelectedValue = selectedDevice.DeviceNumber; }
            OutputSelectionExplicit = _showingLegacyCandidate && _deviceCombo.SelectedIndex >= 0;
        }
        SelectInitialOutput();

        _deviceCombo.SelectionChangeCommitted += (_, _) =>
        {
            _showingLegacyCandidate = false;
            OutputSelectionExplicit = _deviceCombo.SelectedIndex >= 0;
            UpdateOutputConfirmation();
        };
        Shown += (_, _) =>
        {
            SelectInitialOutput();
            UpdateOutputConfirmation();
            if (_requireOutputSelection) { _deviceCombo.Focus(); }
        };
        if (requireOutputSelection)
        {
            _deviceCombo.AccessibleName = "出力デバイスを選び直す";
        }

        var outputControls = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        outputControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outputControls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _refreshDevices = new Button { Text = "更新", AutoSize = true, Anchor = AnchorStyles.Top, Visible = false };
        outputControls.Controls.Add(_deviceCombo, 0, 0);
        outputControls.Controls.Add(_refreshDevices, 1, 0);

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
        _exclusiveGroupsCheck = new CheckBox
        {
            Text = "再生時に同じGroupの音源を止める",
            Checked = settings.ExclusiveGroups,
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
        _outputLabel = new Label { Text = requireOutputSelection ? "出力（要選択）" : "出力デバイス", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(_outputLabel, 0, 2);
        root.Controls.Add(outputControls, 1, 2);
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
        root.Controls.Add(new Label { Text = "Group", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 10);
        root.Controls.Add(_exclusiveGroupsCheck, 1, 10);

        _toolTip.SetToolTip(_themeColorText, "色を#RRGGBB形式で指定します。例: #202020");
        _toolTip.SetToolTip(_backgroundInputCheck, "別のアプリを操作中も、音源の割り当てキーを受け付けます。");
        _toolTip.SetToolTip(_silentLoopCheck, "出力ストリームを維持し、再生の頭切れを防ぎます。");
        _toolTip.SetToolTip(_fadeOutOnStopCheck, "通常の停止時に、その音源のFade Out設定を使います。");
        _toolTip.SetToolTip(_fadeOutOnStopAllCheck, "全停止時に、それぞれの音源のFade Out設定を使います。緊急停止は常に即時停止です。");
        _toolTip.SetToolTip(_exclusiveGroupsCheck, "無印ポン出しくんと同様に、同じGroupで鳴っている音源を停止してから再生します。");
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
        _okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(116, 36) };
        var cancelButton = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(116, 36) };
        _emergencyStop = new Button { Text = "緊急停止", AutoSize = true, Visible = false, Tag = UiTheme.DangerButtonRole };
        buttons.Controls.Add(_okButton);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(_emergencyStop);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(root);
        Controls.Add(scroll);
        Controls.Add(buttons);
        AcceptButton = _okButton;
        CancelButton = cancelButton;
        UpdateOutputConfirmation();
    }

    internal AudioDeviceSnapshot? OutputSnapshot { get; private set; }

    internal void ConfigureOutputRefresh(AudioDeviceSnapshot snapshot, Func<Task<AudioDeviceSnapshot>> capture, Action emergencyStop)
    {
        OutputSnapshot = snapshot;
        _refreshDevices.Visible = true;
        _emergencyStop.Visible = true;
        _emergencyStop.Click += (_, _) => emergencyStop();
        _refreshDevices.Click += async (_, _) => await RefreshOutputAsync(capture);
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += async (_, _) => await RefreshOutputAsync(capture);
        Disposed += (_, _) => timer.Dispose();
        Shown += (_, _) => timer.Start();
    }

    private async Task RefreshOutputAsync(Func<Task<AudioDeviceSnapshot>> capture)
    {
        if (_refreshingDevices || IsDisposed || Disposing) { return; }
        _refreshingDevices = true;
        _refreshDevices.Enabled = false;
        UpdateOutputConfirmation();
        try
        {
            var snapshot = await capture();
            if (IsDisposed || Disposing) { return; }
            var previous = OutputSnapshot;
            OutputSnapshot = snapshot;
            _outputRefreshFailed = false;
            if (previous is null || AudioDeviceSnapshotChanges.Differ(previous, snapshot))
            {
                var selected = _deviceCombo.SelectedItem as DeviceChoice;
                var keepSelection = selected is not null && previous is not null
                    && AudioOutputBinding.Create(selected.SelectionId, previous)?.Matches(snapshot) == true;
                _deviceCombo.DataSource = GetDeviceChoices(snapshot.Devices);
                if (keepSelection) { _deviceCombo.SelectedValue = selected!.DeviceNumber; }
                else
                {
                    _deviceCombo.SelectedIndex = -1;
                    OutputSelectionExplicit = false;
                    _showingLegacyCandidate = false;
                    _requireOutputSelection = true;
                }
            }
            UpdateOutputConfirmation();
        }
        catch (Exception) when (!IsDisposed && !Disposing)
        {
            _outputRefreshFailed = true;
            _deviceCombo.Enabled = false;
            _deviceCombo.SelectedIndex = -1;
            _requireOutputSelection = true;
            _showingLegacyCandidate = false;
            OutputSelectionExplicit = false;
        }
        catch (Exception) when (IsDisposed || Disposing) { }
        finally
        {
            _refreshingDevices = false;
            if (!IsDisposed && !Disposing)
            {
                _refreshDevices.Enabled = true;
                UpdateOutputConfirmation();
            }
        }
    }

    private sealed record DeviceChoice(int DeviceNumber, string SelectionId, string DisplayName);

    private static DeviceChoice[] GetDeviceChoices(IReadOnlyList<AudioDeviceInfo> devices)
    {
        var duplicateNames = devices.GroupBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return devices.Select(device => new DeviceChoice(device.DeviceNumber, device.SelectionId,
            device.DeviceNumber >= 0 && duplicateNames.Contains(device.Name)
                ? $"{device.Name}（番号 {device.DeviceNumber}）" : device.Name)).ToArray();
    }

    private void UpdateOutputConfirmation()
    {
        _outputLabel.Text = _outputRefreshFailed ? "出力（確認失敗）"
            : _showingLegacyCandidate ? "旧出力先（確認）"
            : _requireOutputSelection && !OutputSelectionExplicit ? "出力（要選択）" : "出力デバイス";
        _deviceCombo.Enabled = !_outputRefreshFailed;
        _okButton.Enabled = !_refreshingDevices && !_outputRefreshFailed && (!_requireOutputSelection || OutputSelectionExplicit);
    }

    internal bool OutputSelectionExplicit { get; private set; }

    public int SelectedDeviceNumber => _deviceCombo.SelectedValue is int number ? number : -1;

    public bool BackgroundInput => _backgroundInputCheck.Checked;

    public bool SilentLoop => _silentLoopCheck.Checked;

    public bool FadeOutOnStop => _fadeOutOnStopCheck.Checked;

    public bool FadeOutOnStopAll => _fadeOutOnStopAllCheck.Checked;

    public bool DarkMode => _darkModeCheck.Checked;

    internal bool ExclusiveGroups => _exclusiveGroupsCheck.Checked;

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
