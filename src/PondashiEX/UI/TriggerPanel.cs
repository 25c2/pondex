using PondashiEX.Project;

namespace PondashiEX.UI;

public sealed class TriggerPanel : UserControl
{
    private readonly DataGridView _grid;
    private IReadOnlyList<SoundDefinition> _sounds = [];
    private List<TriggerDefinition> _triggers = [];
    private bool _refreshing;
    private bool _darkMode;

    public TriggerPanel()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            RowTemplate = { Height = 34 },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 248, 251) },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(225, 231, 237),
                Font = UiTheme.CreateFont(10F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft
            },
            EnableHeadersVisualStyles = false
        };
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "有効", DataPropertyName = nameof(TriggerRow.Enabled), Width = 58, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "メモ", DataPropertyName = nameof(TriggerRow.Memo), FillWeight = 24, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "条件", DataPropertyName = nameof(TriggerRow.Condition), FillWeight = 34, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "操作", DataPropertyName = nameof(TriggerRow.Action), FillWeight = 14, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "対象", DataPropertyName = nameof(TriggerRow.Target), FillWeight = 28, ReadOnly = true });
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        var addButton = new Button { Text = "＋ 追加", Width = 90, Height = 32 };
        var editButton = new Button { Text = "編集", Width = 90, Height = 32 };
        var deleteButton = new Button { Text = "削除", Width = 90, Height = 32 };
        var toggleButton = new Button { Text = "有効/無効", Width = 100, Height = 32 };
        toolbar.Controls.AddRange([addButton, editButton, deleteButton, toggleButton]);
        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        Controls.Add(root);

        addButton.Click += (_, _) => AddTrigger();
        editButton.Click += (_, _) => EditSelectedTrigger();
        deleteButton.Click += (_, _) => DeleteSelectedTrigger();
        toggleButton.Click += (_, _) => ToggleSelectedTrigger();
        _grid.CellDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex >= 0 && eventArgs.ColumnIndex != 0)
            {
                EditSelectedTrigger();
            }
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _grid.CellValueChanged += (_, eventArgs) =>
        {
            if (_refreshing || eventArgs.RowIndex < 0 || eventArgs.ColumnIndex != 0
                || _grid.Rows[eventArgs.RowIndex].DataBoundItem is not TriggerRow row)
            {
                return;
            }

            row.Definition.Enabled = row.Enabled;
            DefinitionsChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? DefinitionsChanged;

    internal void ApplyDarkMode(bool darkMode)
    {
        _darkMode = darkMode;
        UiTheme.Apply(this, darkMode);
    }

    public void SetData(List<TriggerDefinition> triggers, IReadOnlyList<SoundDefinition> sounds)
    {
        _triggers = triggers;
        _sounds = sounds;
        RefreshGrid();
    }

    private TriggerDefinition? SelectedTrigger =>
        (_grid.CurrentRow?.DataBoundItem as TriggerRow)?.Definition;

    private void AddTrigger()
    {
        if (_sounds.Count == 0)
        {
            MessageBox.Show(this, "先に音源を追加してください。", "イベントトリガ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var definition = new TriggerDefinition { TargetSoundId = _sounds[0].Id };
        using var dialog = new TriggerDialog(definition, _sounds);
        UiTheme.Apply(dialog, _darkMode);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _triggers.Add(definition);
            Changed();
        }
    }

    private void EditSelectedTrigger()
    {
        var selected = SelectedTrigger;
        if (selected is null)
        {
            return;
        }

        using var dialog = new TriggerDialog(selected, _sounds);
        UiTheme.Apply(dialog, _darkMode);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Changed();
        }
    }

    private void DeleteSelectedTrigger()
    {
        var selected = SelectedTrigger;
        if (selected is null)
        {
            return;
        }

        if (MessageBox.Show(this, "選択したイベントを削除しますか？", "イベントトリガ", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _triggers.Remove(selected);
            Changed();
        }
    }

    private void ToggleSelectedTrigger()
    {
        var selected = SelectedTrigger;
        if (selected is null)
        {
            return;
        }

        selected.Enabled = !selected.Enabled;
        Changed();
    }

    private void Changed()
    {
        RefreshGrid();
        DefinitionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshGrid()
    {
        _refreshing = true;
        try
        {
            _grid.DataSource = null;
            _grid.DataSource = _triggers.Select(trigger => new TriggerRow(
                trigger,
                trigger.Memo,
                DescribeCondition(trigger),
                DescribeAction(trigger.Action),
                DescribeTarget(trigger))).ToList();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private string DescribeCondition(TriggerDefinition trigger) => trigger.TriggerType switch
    {
        TriggerType.ClockTime => $"指定時刻 {trigger.Parameters.ClockTime:HH:mm:ss}",
        TriggerType.StopwatchElapsed => $"SW {trigger.Parameters.Elapsed?.TotalSeconds:0.0}秒経過",
        TriggerType.SoundEnded => $"{SoundName(trigger.Parameters.SourceSoundId)} の再生終了",
        TriggerType.SoundElapsed => $"{SoundName(trigger.Parameters.SourceSoundId)} の再生から {trigger.Parameters.Elapsed?.TotalSeconds:0.0}秒",
        _ => trigger.TriggerType.ToString()
    };

    private static string DescribeAction(TriggerAction action) => action switch
    {
        TriggerAction.Play => "再生",
        TriggerAction.Stop => "停止",
        TriggerAction.Pause => "一時停止",
        _ => action.ToString()
    };

    private string DescribeTarget(TriggerDefinition trigger)
    {
        if (!string.IsNullOrWhiteSpace(trigger.TargetGroup))
        {
            return $"グループ: {trigger.TargetGroup}";
        }

        return SoundName(trigger.TargetSoundId);
    }

    private string SoundName(Guid? soundId) => soundId is { } id
        ? _sounds.FirstOrDefault(sound => sound.Id == id)?.Name ?? $"不明 [{id.ToString()[..8]}]"
        : "未指定";

    private sealed class TriggerRow
    {
        public TriggerRow(TriggerDefinition definition, string memo, string condition, string action, string target)
        {
            Definition = definition;
            Enabled = definition.Enabled;
            Memo = memo;
            Condition = condition;
            Action = action;
            Target = target;
        }

        public TriggerDefinition Definition { get; }

        public bool Enabled { get; set; }

        public string Memo { get; }

        public string Condition { get; }

        public string Action { get; }

        public string Target { get; }
    }
}
