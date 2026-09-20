namespace PondashiEX.Input;

public sealed class ShortcutMessageFilter : IMessageFilter
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private readonly Action<Keys, bool> _keyChanged;
    private readonly Func<bool> _enabled;

    public ShortcutMessageFilter(Action<Keys, bool> keyChanged, Func<bool> enabled)
    {
        _keyChanged = keyChanged;
        _enabled = enabled;
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (!_enabled())
        {
            return false;
        }

        var isDown = m.Msg is WmKeyDown or WmSysKeyDown;
        var isUp = m.Msg is WmKeyUp or WmSysKeyUp;
        if (!isDown && !isUp)
        {
            return false;
        }

        var key = (Keys)(int)m.WParam | Control.ModifierKeys;
        _keyChanged(key, isDown);
        return false;
    }
}
