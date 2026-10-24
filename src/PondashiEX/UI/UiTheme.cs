namespace PondashiEX.UI;

internal static class UiTheme
{
    private const string FontFamilyName = "Meiryo UI";
    public const string AccentButtonRole = "theme:accent";
    public const string DangerButtonRole = "theme:danger";
    public const string StopButtonRole = "theme:stop";
    public const string SuccessButtonRole = "theme:success";
    public const string WarningButtonRole = "theme:warning";
    public const string ElevatedSurfaceRole = "theme:elevated";
    public const string SoundListRole = "theme:sound-list";

    private static readonly Color DarkBackground = Color.FromArgb(30, 32, 35);
    private static readonly Color DarkSurface = Color.FromArgb(42, 45, 49);
    private static readonly Color DarkInput = Color.FromArgb(24, 26, 29);
    private static readonly Color DarkBorder = Color.FromArgb(78, 82, 88);
    private static readonly Color DarkForeground = Color.FromArgb(238, 240, 242);
    private static readonly Color DarkMuted = Color.FromArgb(190, 194, 199);

    public static Font CreateFont(float size, FontStyle style = FontStyle.Regular) =>
        new(FontFamilyName, size, style, GraphicsUnit.Point);

    public static void ConfigureApplicationColors(bool darkMode) =>
        Application.SetColorMode(darkMode ? SystemColorMode.Dark : SystemColorMode.Classic);

    public static void Apply(Control root, bool darkMode)
    {
        ApplyControl(root, darkMode);
    }

    private static void ApplyControl(Control control, bool darkMode)
    {
        NativeWindowTheme.Apply(control, darkMode);
        switch (control)
        {
            case ThemedTabControl tabs:
                tabs.ApplyTheme(darkMode);
                break;
            case ThemedFlowLayoutPanel flow:
                flow.ApplyTheme(darkMode);
                ApplySurface(flow, darkMode);
                break;
            case DataGridView grid:
                ApplyGrid(grid, darkMode);
                break;
            case MenuStrip menu:
                ApplyToolStrip(menu, darkMode);
                break;
            case StatusStrip status:
                ApplyToolStrip(status, darkMode);
                break;
            case TextBoxBase textBox:
                textBox.BackColor = darkMode ? DarkInput : SystemColors.Window;
                textBox.ForeColor = darkMode ? DarkForeground : SystemColors.WindowText;
                break;
            case NumericUpDown numeric:
                numeric.BorderStyle = darkMode ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                numeric.BackColor = darkMode ? DarkInput : SystemColors.Window;
                numeric.ForeColor = darkMode ? DarkForeground : SystemColors.WindowText;
                break;
            case ComboBox combo:
                combo.BackColor = darkMode ? DarkInput : SystemColors.Window;
                combo.ForeColor = darkMode ? DarkForeground : SystemColors.WindowText;
                break;
            case DateTimePicker picker:
                picker.BackColor = darkMode ? DarkInput : SystemColors.Window;
                picker.ForeColor = darkMode ? DarkForeground : SystemColors.WindowText;
                picker.CalendarMonthBackground = darkMode ? DarkInput : SystemColors.Window;
                picker.CalendarForeColor = darkMode ? DarkForeground : SystemColors.WindowText;
                picker.CalendarTitleBackColor = darkMode ? DarkSurface : SystemColors.ActiveCaption;
                picker.CalendarTitleForeColor = darkMode ? DarkForeground : SystemColors.ActiveCaptionText;
                break;
            case ListView list:
                list.BackColor = darkMode ? DarkInput : SystemColors.Window;
                list.ForeColor = darkMode ? DarkForeground : SystemColors.WindowText;
                break;
            case Button button:
                ApplyButton(button, darkMode);
                break;
            case Label label:
                label.ForeColor = darkMode ? DarkForeground : SystemColors.ControlText;
                label.BackColor = Color.Transparent;
                break;
            case CheckBox checkBox:
                // Flat checkbox painting can put a pale check mark on a white box in dark mode.
                // Keep native, state-aware glyph rendering; only button-style toggles need flat borders.
                checkBox.FlatStyle = darkMode && checkBox.Appearance == Appearance.Button
                    ? FlatStyle.Flat : FlatStyle.Standard;
                checkBox.FlatAppearance.BorderColor = darkMode ? DarkBorder : SystemColors.ControlDark;
                checkBox.FlatAppearance.BorderSize = 1;
                checkBox.ForeColor = darkMode ? DarkForeground : SystemColors.ControlText;
                if (checkBox.Appearance != Appearance.Button)
                {
                    checkBox.BackColor = Color.Transparent;
                }
                break;
            case TrackBar trackBar:
                trackBar.BackColor = darkMode ? DarkBackground : SystemColors.Control;
                trackBar.ForeColor = darkMode ? DarkForeground : SystemColors.ControlText;
                break;
            default:
                ApplySurface(control, darkMode);
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyControl(child, darkMode);
        }
    }

    private static void ApplySurface(Control control, bool darkMode)
    {
        if (control is not Form and not UserControl and not Panel and not TableLayoutPanel
            and not FlowLayoutPanel and not TabPage and not TabControl)
        {
            return;
        }

        control.BackColor = control.Tag switch
        {
            ElevatedSurfaceRole => darkMode ? DarkSurface : Color.FromArgb(245, 245, 245),
            SoundListRole => darkMode ? Color.FromArgb(22, 24, 27) : Color.FromArgb(224, 228, 232),
            _ => darkMode ? DarkBackground : SystemColors.Control
        };
        control.ForeColor = darkMode ? DarkForeground : SystemColors.ControlText;
    }

    private static void ApplyButton(Button button, bool darkMode)
    {
        button.FlatStyle = darkMode ? FlatStyle.Flat : FlatStyle.Standard;
        button.FlatAppearance.BorderColor = darkMode ? DarkBorder : SystemColors.ControlDark;
        button.FlatAppearance.BorderSize = 1;
        button.UseVisualStyleBackColor = false;
        (button.BackColor, button.ForeColor) = button.Tag switch
        {
            DangerButtonRole => darkMode
                ? (Color.FromArgb(190, 52, 52), Color.White)
                : (Color.FromArgb(220, 65, 65), Color.White),
            StopButtonRole => darkMode
                ? (Color.FromArgb(105, 48, 52), DarkForeground)
                : (Color.FromArgb(255, 225, 225), SystemColors.ControlText),
            SuccessButtonRole => darkMode
                ? (Color.FromArgb(42, 91, 59), DarkForeground)
                : (Color.FromArgb(215, 245, 215), SystemColors.ControlText),
            WarningButtonRole => darkMode
                ? (Color.FromArgb(105, 86, 38), DarkForeground)
                : (Color.FromArgb(255, 245, 205), SystemColors.ControlText),
            AccentButtonRole => darkMode
                ? (Color.FromArgb(48, 76, 102), DarkForeground)
                : (Color.FromArgb(225, 235, 245), SystemColors.ControlText),
            _ => darkMode
                ? (Color.FromArgb(58, 62, 67), DarkForeground)
                : (SystemColors.Control, SystemColors.ControlText)
        };
        button.FlatAppearance.MouseOverBackColor = darkMode
            ? ControlPaint.Light(button.BackColor, 0.15F) : Color.Empty;
        button.FlatAppearance.MouseDownBackColor = darkMode
            ? ControlPaint.Dark(button.BackColor, 0.15F) : Color.Empty;
    }

    private static void ApplyGrid(DataGridView grid, bool darkMode)
    {
        var background = darkMode ? DarkBackground : SystemColors.Window;
        var cellBackground = darkMode ? DarkInput : SystemColors.Window;
        var alternateBackground = darkMode ? Color.FromArgb(36, 39, 43) : Color.FromArgb(245, 248, 251);
        var headerBackground = darkMode ? DarkSurface : Color.FromArgb(225, 231, 237);
        var foreground = darkMode ? DarkForeground : SystemColors.ControlText;
        grid.BackgroundColor = background;
        grid.GridColor = darkMode ? DarkBorder : SystemColors.ControlDark;
        grid.DefaultCellStyle.BackColor = cellBackground;
        grid.DefaultCellStyle.ForeColor = foreground;
        grid.DefaultCellStyle.SelectionBackColor = darkMode ? Color.FromArgb(58, 92, 126) : SystemColors.Highlight;
        grid.DefaultCellStyle.SelectionForeColor = darkMode ? Color.White : SystemColors.HighlightText;
        grid.RowsDefaultCellStyle.BackColor = cellBackground;
        grid.RowsDefaultCellStyle.ForeColor = foreground;
        grid.AlternatingRowsDefaultCellStyle.BackColor = alternateBackground;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = foreground;
        grid.ColumnHeadersDefaultCellStyle.BackColor = headerBackground;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = foreground;
        grid.EnableHeadersVisualStyles = false;
    }

    private static void ApplyToolStrip(ToolStrip strip, bool darkMode)
    {
        strip.BackColor = darkMode ? DarkSurface : SystemColors.Control;
        strip.ForeColor = darkMode ? DarkForeground : SystemColors.ControlText;
        strip.RenderMode = darkMode ? ToolStripRenderMode.Professional : ToolStripRenderMode.System;
        strip.Renderer = darkMode
            ? new ToolStripProfessionalRenderer(new DarkColorTable())
            : new ToolStripSystemRenderer();
        foreach (ToolStripItem item in strip.Items)
        {
            ApplyToolStripItem(item, darkMode);
        }
    }

    private static void ApplyToolStripItem(ToolStripItem item, bool darkMode)
    {
        if (item is ToolStripStatusLabel status)
        {
            status.BorderSides = darkMode ? ToolStripStatusLabelBorderSides.None : ToolStripStatusLabelBorderSides.Top;
        }

        item.BackColor = darkMode ? DarkSurface : SystemColors.Control;
        item.ForeColor = darkMode ? DarkForeground : SystemColors.ControlText;
        if (item is ToolStripDropDownItem dropDown)
        {
            ApplyToolStrip(dropDown.DropDown, darkMode);
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => DarkSurface;
        public override Color MenuStripGradientEnd => DarkSurface;
        public override Color ToolStripDropDownBackground => DarkSurface;
        public override Color ToolStripBorder => DarkBorder;
        public override Color ImageMarginGradientBegin => DarkSurface;
        public override Color ImageMarginGradientMiddle => DarkSurface;
        public override Color ImageMarginGradientEnd => DarkSurface;
        public override Color MenuItemSelected => Color.FromArgb(63, 68, 74);
        public override Color MenuItemBorder => DarkBorder;
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(53, 57, 62);
        public override Color MenuItemPressedGradientMiddle => Color.FromArgb(53, 57, 62);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(53, 57, 62);
        public override Color SeparatorDark => DarkBorder;
        public override Color SeparatorLight => DarkMuted;
        public override Color StatusStripGradientBegin => DarkSurface;
        public override Color StatusStripGradientEnd => DarkSurface;
    }
}
