using PondashiEX.Project;

namespace PondashiEX.UI;

public sealed class PlaybackSettingsDialog : Form
{
    private readonly NumericUpDown _speed;
    private readonly NumericUpDown _pitch;
    private readonly NumericUpDown _pan;
    private bool _resetting;

    public PlaybackSettingsDialog(SoundDefinition sound)
    {
        Text = $"再生中の調整 — {sound.Name}";
        ClientSize = new Size(690, 330);
        MinimumSize = new Size(480, 310);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        DpiWindowHelper.Attach(this);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 5,
            AutoScroll = true
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 3; row++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        }

        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _speed = AddValue(root, 0, "再生速度（倍）", 0.5m, 2m, (decimal)sound.Speed, 2, 1);
        _pitch = AddValue(root, 1, "音程補正 Pitch（倍）", 0.5m, 2m, (decimal)sound.Pitch, 2, 1);
        _pan = AddValue(root, 2, "Pan（左 −100 ～ 右 +100）", -100, 100, (decimal)(sound.Pan * 100), 0, 0);
        root.Controls.Add(new Label
        {
            Text = "スライダーと数値入力は連動し、再生中の音にも即時反映されます。右クリックで各項目を標準値へ戻せます。\n速度を変えると音程も変わります。Pitchは速度とは別の音程補正です。\n設定はプロジェクトに保存されます。Fade Inは次回再生時、Fade Outは次の停止操作で適用されます。",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 8)
        }, 0, 3);
        root.SetColumnSpan(root.GetControlFromPosition(0, 3)!, 2);
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty
        };
        var reset = new Button { Text = "標準に戻す", Width = 140, Height = 32 };
        var close = new Button { Text = "閉じる", DialogResult = DialogResult.OK, Width = 110, Height = 32 };
        reset.Click += (_, _) => ResetValues();
        footer.Controls.Add(close);
        footer.Controls.Add(reset);
        root.Controls.Add(footer, 0, 4);
        root.SetColumnSpan(footer, 2);
        Controls.Add(root);
        CancelButton = close;
        _speed.ValueChanged += (_, _) => NotifySettingsChanged();
        _pitch.ValueChanged += (_, _) => NotifySettingsChanged();
        _pan.ValueChanged += (_, _) => NotifySettingsChanged();
    }

    public event EventHandler? SettingsChanged;

    public float Speed => (float)_speed.Value;

    public float Pitch => (float)_pitch.Value;

    public float Pan => (float)_pan.Value / 100;

    private void ResetValues()
    {
        var changed = _speed.Value != 1 || _pitch.Value != 1 || _pan.Value != 0;
        _resetting = true;
        try
        {
            _speed.Value = 1;
            _pitch.Value = 1;
            _pan.Value = 0;
        }
        finally
        {
            _resetting = false;
        }

        if (changed)
        {
            NotifySettingsChanged();
        }
    }

    private void NotifySettingsChanged()
    {
        if (!_resetting)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static NumericUpDown AddValue(TableLayoutPanel root, int row, string caption,
        decimal minimum, decimal maximum, decimal value, int decimals, decimal defaultValue)
    {
        root.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        var scale = decimals == 0 ? 1 : 100;
        var initialValue = decimal.Round(Math.Clamp(value, minimum, maximum), decimals);
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        var slider = new TrackBar
        {
            Minimum = (int)(minimum * scale),
            Maximum = (int)(maximum * scale),
            Value = (int)(initialValue * scale),
            SmallChange = 1,
            LargeChange = 10,
            TickStyle = TickStyle.None,
            Dock = DockStyle.Fill,
            AccessibleName = caption + " スライダー"
        };
        var control = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = initialValue,
            DecimalPlaces = decimals,
            Increment = decimals == 0 ? 1 : 0.01m,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AccessibleName = caption + " 数値"
        };
        slider.ValueChanged += (_, _) => control.Value = (decimal)slider.Value / scale;
        control.ValueChanged += (_, _) =>
        {
            var position = (int)decimal.Round(control.Value * scale);
            if (slider.Value != position)
            {
                slider.Value = position;
            }
        };
        slider.MouseDown += (_, args) =>
        {
            if (args.Button == MouseButtons.Right)
            {
                control.Value = defaultValue;
            }
        };
        editor.Controls.Add(slider, 0, 0);
        editor.Controls.Add(control, 1, 0);
        root.Controls.Add(editor, 1, row);
        return control;
    }
}
