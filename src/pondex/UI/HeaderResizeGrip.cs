namespace pondex.UI;

internal sealed class HeaderResizeGrip : Control
{
    private int _lastY;
    private bool _dragging;

    internal HeaderResizeGrip()
    {
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Cursor = Cursors.SizeNS;
        AccessibleName = "ヘッダーの高さ";
        AccessibleDescription = "上下にドラッグ。上下キーでも変更できます。Homeで標準の高さに戻ります。";
        TabStop = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer | ControlStyles.StandardDoubleClick, true);
        ContextMenuStrip = new ContextMenuStrip();
        ContextMenuStrip.Items.Add("標準の高さに戻す", null, (_, _) => ResetRequested?.Invoke());
    }

    internal event Action<int>? ResizedBy;
    internal event Action? ResizeFinished;
    internal event Action? ResetRequested;

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Modifiers != Keys.None) { return; }
        switch (e.KeyCode)
        {
            case Keys.Home:
                ResetRequested?.Invoke();
                break;
            case Keys.Up:
            case Keys.Down:
                ResizedBy?.Invoke((e.KeyCode == Keys.Up ? -1 : 1) * Math.Max(1, 8 * DeviceDpi / 96));
                ResizeFinished?.Invoke();
                break;
            default: return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) { return; }
        Capture = true;
        _dragging = true;
        _lastY = PointToScreen(e.Location).Y;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || !Capture) { return; }
        var screenY = PointToScreen(e.Location).Y;
        var delta = screenY - _lastY;
        _lastY = screenY;
        if (delta != 0) { ResizedBy?.Invoke(delta); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && _dragging) { Capture = false; }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!_dragging || Capture) { return; }
        _dragging = false;
        ResizeFinished?.Invoke();
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        ResetRequested?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var center = Height / 2;
        using var pen = new Pen(Focused ? SystemColors.Highlight : ForeColor);
        e.Graphics.DrawLine(pen, Width / 2 - 18, center, Width / 2 + 18, center);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { ContextMenuStrip?.Dispose(); }
        base.Dispose(disposing);
    }
}
