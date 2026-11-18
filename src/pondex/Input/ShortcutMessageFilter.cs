namespace pondex.Input;

public sealed class ShortcutMessageFilter : IMessageFilter
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private readonly Func<Keys, bool, bool, bool> _keyChanged;
    private readonly Func<IntPtr, bool> _enabled;

    public ShortcutMessageFilter(Func<Keys, bool, bool, bool> keyChanged, Func<IntPtr, bool> enabled)
    {
        _keyChanged = keyChanged;
        _enabled = enabled;
    }

    public bool PreFilterMessage(ref Message m)
    {
        var isDown = m.Msg is WmKeyDown or WmSysKeyDown;
        var isUp = m.Msg is WmKeyUp or WmSysKeyUp;
        if ((!isDown && !isUp) || !_enabled(m.HWnd))
        {
            return false;
        }

        var key = (Keys)(int)m.WParam | Control.ModifierKeys;
        var isRepeat = isDown && (m.LParam.ToInt64() & (1L << 30)) != 0;
        return _keyChanged(key, isDown, isRepeat);
    }
}
