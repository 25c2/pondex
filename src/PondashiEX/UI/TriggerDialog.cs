using PondashiEX.Project;

namespace PondashiEX.UI;

public sealed class TriggerDialog : Form
{
    private readonly TextBox _memoText;
    private readonly ComboBox _typeCombo;
    private readonly DateTimePicker _clockTime;
    private readonly NumericUpDown _elapsedSeconds;
    private readonly ComboBox _sourceSoundCombo;
    private readonly ComboBox _actionCombo;
    private readonly ComboBox _targetSoundCombo;
    private readonly CheckBox _enabledCheck;

    public TriggerDialog(
        TriggerDefinition definition,
        IReadOnlyList<SoundDefinition> sounds)
    {
        Definition = definition;
        Text = "イベントトリガ";
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(580, 390);
        MinimumSize = new Size(440, 280);
        DpiWindowHelper.Attach(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 9,
            AutoScroll = true
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 8; row++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        }
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _enabledCheck = new CheckBox { Text = "有効", Checked = definition.Enabled, AutoSize = true };
        _memoText = new TextBox { Text = definition.Memo, Dock = DockStyle.Fill, MaxLength = ProjectLimits.MaxMemoLength };
        _typeCombo = CreateEnumCombo<TriggerType>(definition.TriggerType);
        _clockTime = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm:ss",
            ShowUpDown = true,
            Width = 120,
            Value = DateTime.Today + (definition.Parameters.ClockTime?.ToTimeSpan() ?? TimeSpan.Zero)
        };
        _elapsedSeconds = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 7 * 24 * 60 * 60,
            DecimalPlaces = 1,
            Increment = 0.1m,
            Value = (decimal)Math.Clamp(
                definition.Parameters.Elapsed?.TotalSeconds ?? 0,
                0,
                7 * 24 * 60 * 60),
            Width = 140
        };
        var soundItems = sounds.Select(sound => new SoundItem(sound.Id, sound.Name)).ToList();
        _sourceSoundCombo = CreateSoundCombo(soundItems, definition.Parameters.SourceSoundId);
        _actionCombo = CreateEnumCombo<TriggerAction>(definition.Action);
        var targetItems = soundItems
            .Select(sound => new TargetItem(sound.Id, null, $"音源: {sound.DisplayName}"))
            .Concat(sounds
                .Select(sound => sound.Group.Trim())
                .Where(group => group.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(group => new TargetItem(null, group, $"グループ: {group}")))
            .ToList();
        _targetSoundCombo = CreateTargetCombo(targetItems, definition.TargetSoundId, definition.TargetGroup);

        AddRow(root, 0, "状態", _enabledCheck);
        AddRow(root, 1, "メモ", _memoText);
        AddRow(root, 2, "トリガ条件", _typeCombo);
        AddRow(root, 3, "指定時刻", _clockTime);
        AddRow(root, 4, "経過秒", _elapsedSeconds);
        AddRow(root, 5, "条件の音源", _sourceSoundCombo);
        AddRow(root, 6, "アクション", _actionCombo);
        AddRow(root, 7, "対象音源", _targetSoundCombo);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var okButton = new Button { Text = "OK", Width = 90, Height = 32 };
        var cancelButton = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Width = 90, Height = 32 };
        okButton.Click += (_, _) => SaveAndClose();
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        root.Controls.Add(buttons, 0, 8);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        _typeCombo.SelectedValueChanged += (_, _) => UpdateConditionControls();
        UpdateConditionControls();
    }

    public TriggerDefinition Definition { get; }

    private void SaveAndClose()
    {
        if (_targetSoundCombo.SelectedItem is not TargetItem { Missing: false } target)
        {
            MessageBox.Show(this, "アクション対象の音源を選択してください。", "入力確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var type = (TriggerType)_typeCombo.SelectedItem!;
        var action = (TriggerAction)_actionCombo.SelectedItem!;
        var needsSource = type is TriggerType.SoundEnded or TriggerType.SoundElapsed;
        if (needsSource && _sourceSoundCombo.SelectedItem is not SoundItem { Missing: false })
        {
            MessageBox.Show(this, "条件となる音源を選択してください。", "入力確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Definition.Enabled = _enabledCheck.Checked;
        Definition.Memo = _memoText.Text;
        Definition.TriggerType = type;
        Definition.Action = action;
        Definition.TargetSoundId = target.SoundId ?? Guid.Empty;
        Definition.TargetGroup = target.Group ?? string.Empty;
        Definition.Parameters = new TriggerParameters
        {
            ClockTime = type == TriggerType.ClockTime ? TimeOnly.FromDateTime(_clockTime.Value) : null,
            Elapsed = type is TriggerType.StopwatchElapsed or TriggerType.SoundElapsed
                ? TimeSpan.FromSeconds((double)_elapsedSeconds.Value)
                : null,
            SourceSoundId = needsSource && _sourceSoundCombo.SelectedItem is SoundItem source
                ? source.Id
                : null
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateConditionControls()
    {
        if (_typeCombo.SelectedItem is not TriggerType type)
        {
            _clockTime.Enabled = false;
            _elapsedSeconds.Enabled = false;
            _sourceSoundCombo.Enabled = false;
            return;
        }

        _clockTime.Enabled = type == TriggerType.ClockTime;
        _elapsedSeconds.Enabled = type is TriggerType.StopwatchElapsed or TriggerType.SoundElapsed;
        _sourceSoundCombo.Enabled = type is TriggerType.SoundEnded or TriggerType.SoundElapsed;
    }

    private static ComboBox CreateEnumCombo<TEnum>(TEnum selected)
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill
        };
        combo.Items.AddRange(values.Cast<object>().ToArray());
        combo.SelectedItem = selected;
        if (combo.SelectedIndex < 0 && values.Length > 0)
        {
            combo.SelectedIndex = 0;
        }

        combo.Format += (_, eventArgs) => eventArgs.Value = DisplayEnum(eventArgs.ListItem);
        return combo;
    }

    private static string DisplayEnum(object? value) => value switch
    {
        TriggerType.ClockTime => "指定時刻",
        TriggerType.StopwatchElapsed => "ストップウォッチ経過時間",
        TriggerType.SoundEnded => "音源の再生終了",
        TriggerType.SoundElapsed => "音源の再生経過時間",
        TriggerAction.Play => "再生",
        TriggerAction.Stop => "停止",
        TriggerAction.Pause => "一時停止",
        _ => value?.ToString() ?? string.Empty
    };

    private static ComboBox CreateSoundCombo(IReadOnlyList<SoundItem> sounds, Guid? selectedId)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            DisplayMember = nameof(SoundItem.DisplayName)
        };
        combo.Items.AddRange(sounds.Cast<object>().ToArray());
        if (selectedId is { } id)
        {
            combo.SelectedItem = sounds.FirstOrDefault(sound => sound.Id == id);
            if (combo.SelectedIndex < 0)
            {
                var missing = new SoundItem(id, "不明な音源", Missing: true);
                combo.Items.Add(missing);
                combo.SelectedItem = missing;
            }
        }

        if (combo.SelectedIndex < 0 && combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }

        return combo;
    }

    private static ComboBox CreateTargetCombo(
        IReadOnlyList<TargetItem> targets,
        Guid selectedSoundId,
        string selectedGroup)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            DisplayMember = nameof(TargetItem.DisplayName)
        };
        combo.Items.AddRange(targets.Cast<object>().ToArray());
        combo.SelectedItem = !string.IsNullOrWhiteSpace(selectedGroup)
            ? targets.FirstOrDefault(target => string.Equals(target.Group, selectedGroup, StringComparison.OrdinalIgnoreCase))
            : targets.FirstOrDefault(target => target.SoundId == selectedSoundId);
        if (combo.SelectedIndex < 0 && (selectedSoundId != Guid.Empty || !string.IsNullOrWhiteSpace(selectedGroup)))
        {
            var missing = new TargetItem(selectedSoundId, selectedGroup,
                !string.IsNullOrWhiteSpace(selectedGroup) ? $"不明なグループ: {selectedGroup}" : $"不明な音源: {selectedSoundId}",
                Missing: true);
            combo.Items.Add(missing);
            combo.SelectedItem = missing;
        }
        if (combo.SelectedIndex < 0 && combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }

        return combo;
    }

    private static void AddRow(TableLayoutPanel root, int row, string label, Control control)
    {
        root.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        root.Controls.Add(control, 1, row);
    }

    private sealed record SoundItem(Guid Id, string Name, bool Missing = false)
    {
        public string DisplayName => $"{Name}  [{Id.ToString()[..8]}]";
    }

    private sealed record TargetItem(Guid? SoundId, string? Group, string DisplayName, bool Missing = false);
}
