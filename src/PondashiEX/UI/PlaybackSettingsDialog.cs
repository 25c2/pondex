using PondashiEX.Project;

namespace PondashiEX.UI;

public sealed class PlaybackSettingsDialog : Form
{
    private readonly NumericUpDown _speed;
    private readonly NumericUpDown _pitch;
    private readonly NumericUpDown _pan;

    public PlaybackSettingsDialog(SoundDefinition sound)
    {
        Text = $"再生中の調整 — {sound.Name}";
        ClientSize = new Size(510, 290);
        MinimumSize = new Size(420, 280);
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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        for (var row = 0; row < 3; row++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        }

        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _speed = AddValue(root, 0, "再生速度（倍）", 0.5m, 2m, (decimal)sound.Speed, 2);
        _pitch = AddValue(root, 1, "音程補正 Pitch（倍）", 0.5m, 2m, (decimal)sound.Pitch, 2);
        _pan = AddValue(root, 2, "Pan（左 −100 ～ 右 +100）", -100, 100, (decimal)(sound.Pan * 100), 0);
        root.Controls.Add(new Label
        {
            Text = "変更は再生中の音にも即時反映され、プロジェクトに保存されます。\n速度を変えると音程も変わります。Pitchは速度とは別の音程補正です。\nFade Inは次回再生時、Fade Outは次の停止操作で適用されます。",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 8)
        }, 0, 3);
        root.SetColumnSpan(root.GetControlFromPosition(0, 3)!, 2);
        var reset = new Button { Text = "標準に戻す", Dock = DockStyle.Fill };
        var close = new Button { Text = "閉じる", DialogResult = DialogResult.OK, Dock = DockStyle.Fill };
        reset.Click += (_, _) => { _speed.Value = 1; _pitch.Value = 1; _pan.Value = 0; };
        root.Controls.Add(reset, 0, 4);
        root.Controls.Add(close, 1, 4);
        Controls.Add(root);
        CancelButton = close;
        _speed.ValueChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        _pitch.ValueChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        _pan.ValueChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SettingsChanged;

    public float Speed => (float)_speed.Value;

    public float Pitch => (float)_pitch.Value;

    public float Pan => (float)_pan.Value / 100;

    private static NumericUpDown AddValue(TableLayoutPanel root, int row, string caption,
        decimal minimum, decimal maximum, decimal value, int decimals)
    {
        root.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        var control = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(value, minimum, maximum),
            DecimalPlaces = decimals,
            Increment = decimals == 0 ? 1 : 0.05m,
            Dock = DockStyle.Fill
        };
        root.Controls.Add(control, 1, row);
        return control;
    }
}
