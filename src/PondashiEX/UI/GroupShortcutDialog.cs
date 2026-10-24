using PondashiEX.Project;
using PondashiEX.Input;

namespace PondashiEX.UI;

public sealed class GroupShortcutDialog : Form
{
    private readonly ComboBox _groupCombo;
    private readonly TextBox _keyText;
    private readonly ListView _shortcutList;
    private readonly Button _addButton;

    public GroupShortcutDialog(
        IEnumerable<GroupShortcutDefinition> definitions,
        IEnumerable<string> groups)
    {
        Text = "グループショートカット";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(540, 360);
        ClientSize = new Size(620, 430);
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowInTaskbar = false;

        var definitionCopies = definitions
            .Select(definition => new GroupShortcutDefinition
            {
                Group = definition.Group,
                Key = definition.Key
            })
            .ToArray();
        var availableGroups = groups
            .Concat(definitionCopies.Select(definition => definition.Group))
            .Select(group => group.Trim())
            .Where(group => group.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var description = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(570, 0),
            Margin = new Padding(0, 0, 0, 12),
            Text = "グループとキーを登録すると、キーだけでグループ内の全音源を同時再生できます。\nShift＋キーでグループ内の全音源を停止します。"
        };
        root.Controls.Add(description, 0, 0);

        var editor = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 5,
            Margin = new Padding(0, 0, 0, 10)
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editor.Controls.Add(CreateEditorLabel("グループ"), 0, 0);
        _groupCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(6, 3, 14, 3)
        };
        _groupCombo.Items.AddRange(availableGroups.Cast<object>().ToArray());
        if (_groupCombo.Items.Count > 0)
        {
            _groupCombo.SelectedIndex = 0;
        }

        editor.Controls.Add(_groupCombo, 1, 0);
        editor.Controls.Add(CreateEditorLabel("キー"), 2, 0);
        _keyText = new TextBox
        {
            Dock = DockStyle.Fill,
            MaxLength = 20,
            Margin = new Padding(6, 3, 14, 3)
        };
        editor.Controls.Add(_keyText, 3, 0);
        var addButton = new Button
        {
            Text = "追加",
            AutoSize = true,
            Padding = new Padding(10, 3, 10, 3)
        };
        _addButton = addButton;
        addButton.Click += (_, _) => AddShortcut();
        editor.Controls.Add(addButton, 4, 0);
        root.Controls.Add(editor, 0, 1);

        _shortcutList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true
        };
        _shortcutList.Columns.Add("グループ", 360);
        _shortcutList.Columns.Add("キー", 150);
        foreach (var definition in definitionCopies)
        {
            AddListItem(definition);
        }

        root.Controls.Add(_shortcutList, 0, 2);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0)
        };
        var cancelButton = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Padding = new Padding(10, 3, 10, 3)
        };
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Padding = new Padding(18, 3, 18, 3)
        };
        var deleteButton = new Button
        {
            Text = "選択を削除",
            AutoSize = true,
            Padding = new Padding(10, 3, 10, 3),
            Margin = new Padding(0, 3, 16, 3)
        };
        deleteButton.Click += (_, _) => DeleteSelectedShortcut();
        footer.Controls.Add(cancelButton);
        footer.Controls.Add(okButton);
        footer.Controls.Add(deleteButton);
        root.Controls.Add(footer, 0, 3);

        Controls.Add(root);
        AcceptButton = addButton;
        CancelButton = cancelButton;
    }

    public IReadOnlyList<GroupShortcutDefinition> Definitions => _shortcutList.Items
        .Cast<ListViewItem>()
        .Select(item => (GroupShortcutDefinition)item.Tag!)
        .Select(definition => new GroupShortcutDefinition
        {
            Group = definition.Group,
            Key = definition.Key
        })
        .ToArray();

    private void AddShortcut()
    {
        if (_shortcutList.Items.Count >= ProjectLimits.MaxGroupShortcuts)
        {
            return;
        }

        if (_groupCombo.SelectedItem is not string group)
        {
            MessageBox.Show(this, "先に音源へグループを設定してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ShortcutKey.TryNormalize(_keyText.Text, out var key))
        {
            MessageBox.Show(this, "キーは A、2、F1 など、修飾キーを含まない1つのキーで指定してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _keyText.Focus();
            return;
        }

        var duplicate = Definitions.Any(definition =>
            string.Equals(definition.Group, group, StringComparison.OrdinalIgnoreCase)
            && string.Equals(definition.Key, key, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            MessageBox.Show(this, "同じグループとキーが既に登録されています。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        AddListItem(new GroupShortcutDefinition { Group = group, Key = key });
        _keyText.Clear();
        _keyText.Focus();
    }

    private void DeleteSelectedShortcut()
    {
        if (_shortcutList.SelectedItems.Count > 0)
        {
            _shortcutList.Items.Remove(_shortcutList.SelectedItems[0]);
            _addButton.Enabled = _shortcutList.Items.Count < ProjectLimits.MaxGroupShortcuts;
        }
    }

    private void AddListItem(GroupShortcutDefinition definition)
    {
        var item = new ListViewItem(definition.Group)
        {
            Tag = definition
        };
        item.SubItems.Add(definition.Key);
        _shortcutList.Items.Add(item);
        _addButton.Enabled = _shortcutList.Items.Count < ProjectLimits.MaxGroupShortcuts;
    }

    private static Label CreateEditorLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        TextAlign = ContentAlignment.MiddleLeft
    };
}
