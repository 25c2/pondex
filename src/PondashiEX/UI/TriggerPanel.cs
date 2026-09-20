using PondashiEX.Project;

namespace PondashiEX.UI;

public sealed class TriggerPanel : UserControl
{
    private readonly DataGridView _grid;
    private IReadOnlyList<SoundDefinition> _sounds = [];
    private List<TriggerDefinition> _triggers = [];

    public TriggerPanel()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window
        };
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "有効", DataPropertyName = "Enabled", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "メモ", DataPropertyName = "Memo", FillWeight = 35 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "条件", DataPropertyName = "TriggerType", FillWeight = 24 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "操作", DataPropertyName = "Action", FillWeight = 18 });
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
        _grid.CellDoubleClick += (_, _) => EditSelectedTrigger();
    }

    public event EventHandler? DefinitionsChanged;

    public void SetData(List<TriggerDefinition> triggers, IReadOnlyList<SoundDefinition> sounds)
    {
        _triggers = triggers;
        _sounds = sounds;
        RefreshGrid();
    }

    private TriggerDefinition? SelectedTrigger =>
        _grid.CurrentRow?.DataBoundItem as TriggerDefinition;

    private void AddTrigger()
    {
        if (_sounds.Count == 0)
        {
            MessageBox.Show(this, "先に音源を追加してください。", "イベントトリガ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var definition = new TriggerDefinition { TargetSoundId = _sounds[0].Id };
        using var dialog = new TriggerDialog(definition, _sounds);
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
        _grid.DataSource = null;
        _grid.DataSource = _triggers.ToList();
    }
}
