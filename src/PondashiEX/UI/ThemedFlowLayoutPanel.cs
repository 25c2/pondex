namespace PondashiEX.UI;

internal sealed class ThemedFlowLayoutPanel : FlowLayoutPanel
{
    private bool? _darkMode;
    private readonly SmoothScrollMotion _scrollMotion = new();
    private readonly System.Windows.Forms.Timer _scrollTimer = new() { Interval = 15 };
    private long _lastScrollTick;
    private bool _applyingScroll;

    public ThemedFlowLayoutPanel()
    {
        // Opt our composed sound list into WinForms' native scrollbar theming.
        SetStyle(ControlStyles.ApplyThemingImplicitly, true);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        _scrollTimer.Tick += (_, _) =>
        {
            var now = TimeProvider.System.GetTimestamp();
            AdvanceScrolling(TimeProvider.System.GetElapsedTime(_lastScrollTick, now));
            _lastScrollTick = now;
        };
    }

    public void ApplyTheme(bool darkMode)
    {
        if (_darkMode == darkMode)
        {
            return;
        }

        var recreate = _darkMode.HasValue && IsHandleCreated;
        _darkMode = darkMode;
        if (recreate)
        {
            CancelScrolling();
            // Native scrollbars choose their color mode at handle creation.
            var scrollPosition = AutoScrollPosition;
            RecreateHandle();
            AutoScrollPosition = new Point(-scrollPosition.X, -scrollPosition.Y);
        }
    }

    internal bool IsSmoothScrolling => _scrollMotion.IsAnimating;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs { Handled: true })
        {
            return;
        }

        if (!VerticalScroll.Visible)
        {
            base.OnMouseWheel(e);
            return;
        }

        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }

        var lines = SystemInformation.MouseWheelScrollLines;
        if (lines == 0 || e.Delta == 0)
        {
            return;
        }

        var distance = lines < 0 ? ClientSize.Height : lines * (double)Math.Max(Font.Height + 2, 16 * DeviceDpi / 96);
        var offset = -e.Delta / (double)SystemInformation.MouseWheelScrollDelta * distance;
        _scrollMotion.AddOffset(offset, -AutoScrollPosition.Y, MaximumScrollPosition);
        if (_scrollMotion.IsAnimating && !_scrollTimer.Enabled)
        {
            _lastScrollTick = TimeProvider.System.GetTimestamp();
            _scrollTimer.Start();
        }
    }

    internal void AdvanceScrolling(TimeSpan elapsed)
    {
        // Explicit scrolling or focus navigation always takes precedence over animation.
        if (-AutoScrollPosition.Y != _scrollMotion.Position)
        {
            CancelScrolling();
            return;
        }

        var next = _scrollMotion.Advance(elapsed, MaximumScrollPosition);
        _applyingScroll = true;
        try
        {
            if (-AutoScrollPosition.Y != next)
            {
                AutoScrollPosition = new Point(-AutoScrollPosition.X, next);
            }
        }
        finally
        {
            _applyingScroll = false;
        }

        if (!_scrollMotion.IsAnimating)
        {
            _scrollTimer.Stop();
        }
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        if (!_applyingScroll)
        {
            CancelScrolling();
        }

        base.OnScroll(se);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (!_applyingScroll)
        {
            CancelScrolling();
        }
    }

    protected override Point ScrollToControl(Control activeControl)
    {
        CancelScrolling();
        return base.ScrollToControl(activeControl);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        if (!Visible)
        {
            CancelScrolling();
        }

        base.OnVisibleChanged(e);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        CancelScrolling();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x00A1 /* WM_NCLBUTTONDOWN */ && m.WParam.ToInt64() is 6 or 7 /* HTHSCROLL / HTVSCROLL */)
        {
            CancelScrolling();
        }

        if (m.Msg == 0x0115 /* WM_VSCROLL */)
        {
            // Update the contents continuously while the native scrollbar thumb is dragged.
            SetScrollState(ScrollStateFullDrag, true);
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scrollTimer.Stop();
            _scrollTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private int MaximumScrollPosition => VerticalScroll.Visible
        ? Math.Max(0, VerticalScroll.Maximum - VerticalScroll.LargeChange + 1) : 0;

    private void CancelScrolling()
    {
        // Layout and handle callbacks can run while the base control is being constructed.
        if (_scrollTimer is null || _scrollMotion is null)
        {
            return;
        }

        _scrollTimer.Stop();
        _scrollMotion.Reset(-AutoScrollPosition.Y, MaximumScrollPosition);
    }
}
