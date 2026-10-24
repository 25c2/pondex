using System.Globalization;
using PondashiEX.Project;

namespace PondashiEX.UI;

public sealed class SoundOrderDialog : Form
{
    private readonly List<SoundDefinition> _sounds;
    private readonly ListView _list;
    private readonly NumericUpDown _position;
    private readonly Button _up;
    private readonly Button _down;
    private readonly Button _first;
    private readonly Button _last;
    private readonly Button _move;
    private bool _refreshing;

    public SoundOrderDialog(IEnumerable<SoundDefinition> sounds)
    {
        _sounds = sounds.ToList();
        Text = "音源の並び替え";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(520, 380);
        ClientSize = new Size(760, 540);
        ShowInTaskbar = false;
        MinimizeBox = false;
        DpiWindowHelper.Attach(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = "音源を選んで移動します。複数列表示でも、この順に左から右、次の行へ並びます。\n「適用」するまで元の並び順は変わりません。Alt＋↑／↓でも移動できます。",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true,
            Sorting = SortOrder.None
        };
        _list.Columns.Add("順番", 56);
        _list.Columns.Add("音源名", 380);
        _list.Columns.Add("グループ", 150);
        _list.Columns.Add("キー", 90);
        _list.SizeChanged += (_, _) =>
        {
            var fixedWidth = _list.Columns.Cast<ColumnHeader>().Where(column => column.Index != 1).Sum(column => column.Width);
            _list.Columns[1].Width = Math.Max(160, _list.ClientSize.Width - fixedWidth - SystemInformation.VerticalScrollBarWidth - 4);
        };
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        root.Controls.Add(_list, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 10, 0, 4) };
        _up = AddButton(actions, "↑ 上へ", () => MoveSelectedTo(SelectedIndex - 1));
        _down = AddButton(actions, "↓ 下へ", () => MoveSelectedTo(SelectedIndex + 1));
        _first = AddButton(actions, "先頭へ", () => MoveSelectedTo(0));
        _last = AddButton(actions, "末尾へ", () => MoveSelectedTo(_sounds.Count - 1));
        var positionField = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(8, 3, 0, 3) };
        positionField.Controls.Add(new Label { Text = "移動先", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _position = new NumericUpDown
        {
            Minimum = 1,
            Maximum = Math.Max(1, _sounds.Count),
            Value = 1,
            Width = 70,
            TextAlign = HorizontalAlignment.Right
        };
        positionField.Controls.Add(_position);
        _move = AddButton(positionField, "番へ移動", () => MoveSelectedTo((int)_position.Value - 1));
        actions.Controls.Add(positionField);
        root.Controls.Add(actions, 0, 2);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 6, 0, 0)
        };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        var apply = new Button { Text = "適用", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(16, 4, 16, 4) };
        footer.Controls.Add(cancel);
        footer.Controls.Add(apply);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
        CancelButton = cancel;
        // Enter in the position field moves the selected sound, without closing the dialog.
        _position.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                MoveSelectedTo((int)_position.Value - 1);
                args.SuppressKeyPress = true;
            }
        };
        Shown += (_, _) => SelectSound(_sounds.FirstOrDefault()?.Id);
        RefreshList(null);
    }

    public IReadOnlyList<Guid> OrderedSoundIds => _sounds.Select(sound => sound.Id).ToArray();

    private int SelectedIndex => _list.SelectedIndices.Count == 1 ? _list.SelectedIndices[0] : -1;

    internal bool MoveSelectedTo(int targetIndex)
    {
        var sourceIndex = SelectedIndex;
        if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= _sounds.Count || sourceIndex == targetIndex)
        {
            return false;
        }

        var sourceId = _sounds[sourceIndex].Id;
        if (!MainForm.ReorderSoundDefinitions(_sounds, sourceId, _sounds[targetIndex].Id,
                insertAfter: targetIndex > sourceIndex, out _))
        {
            return false;
        }

        RefreshList(sourceId);
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var target = keyData switch
        {
            Keys.Alt | Keys.Up => SelectedIndex - 1,
            Keys.Alt | Keys.Down => SelectedIndex + 1,
            _ => (int?)null
        };
        if (target is { } index)
        {
            MoveSelectedTo(index);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void RefreshList(Guid? selectedId)
    {
        _refreshing = true;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            for (var index = 0; index < _sounds.Count; index++)
            {
                var sound = _sounds[index];
                var item = new ListViewItem([(index + 1).ToString(CultureInfo.InvariantCulture), sound.Name, sound.Group, sound.Key]) { Tag = sound.Id };
                _list.Items.Add(item);
            }

            SelectSound(selectedId);
        }
        finally
        {
            _list.EndUpdate();
            _refreshing = false;
        }

        UpdateButtons();
    }

    private void SelectSound(Guid? soundId)
    {
        var item = _list.Items.Cast<ListViewItem>().FirstOrDefault(item => item.Tag is Guid id && id == soundId);
        if (item is not null)
        {
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
        }
    }

    private void UpdateButtons()
    {
        if (_refreshing)
        {
            return;
        }

        var selected = SelectedIndex;
        _up.Enabled = _first.Enabled = selected > 0;
        _down.Enabled = _last.Enabled = selected >= 0 && selected < _sounds.Count - 1;
        _move.Enabled = _position.Enabled = selected >= 0;
        if (selected >= 0)
        {
            _position.Value = selected + 1;
        }
    }

    private static Button AddButton(FlowLayoutPanel panel, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 3, 6, 3) };
        button.Click += (_, _) => action();
        panel.Controls.Add(button);
        return button;
    }
}
