namespace PondashiEX.UI;

// The native TabControl ignores BackColor and leaves its unused strip and frame light.
internal sealed class ThemedTabControl : TabControl
{
    private bool _darkMode;

    public ThemedTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void ApplyTheme(bool darkMode)
    {
        _darkMode = darkMode;
        Invalidate();
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var background = _darkMode ? Color.FromArgb(30, 32, 35) : SystemColors.Control;
        var border = _darkMode ? Color.FromArgb(78, 82, 88) : SystemColors.ControlDark;
        var foreground = _darkMode ? Color.FromArgb(238, 240, 242) : SystemColors.ControlText;
        e.Graphics.Clear(background);
        var frame = DisplayRectangle;
        frame.Inflate(2, 2);
        ControlPaint.DrawBorder(e.Graphics, frame, border, ButtonBorderStyle.Solid);
        for (var index = 0; index < TabCount; index++)
        {
            var bounds = GetTabRect(index);
            var selected = index == SelectedIndex;
            var tabBackground = _darkMode
                ? selected ? Color.FromArgb(48, 52, 57) : Color.FromArgb(34, 37, 40)
                : selected ? SystemColors.ControlLightLight : SystemColors.Control;
            using var brush = new SolidBrush(tabBackground);
            e.Graphics.FillRectangle(brush, bounds);
            TextRenderer.DrawText(e.Graphics, TabPages[index].Text, Font, bounds, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (selected)
            {
                using var accent = new SolidBrush(_darkMode ? Color.FromArgb(80, 155, 220) : SystemColors.Highlight);
                e.Graphics.FillRectangle(accent, bounds.Left, bounds.Bottom - 2, bounds.Width, 2);
                if (Focused && ShowFocusCues)
                {
                    var focus = bounds;
                    focus.Inflate(-3, -3);
                    ControlPaint.DrawFocusRectangle(e.Graphics, focus, foreground, tabBackground);
                }
            }
        }

        base.OnPaint(e);
    }
}
