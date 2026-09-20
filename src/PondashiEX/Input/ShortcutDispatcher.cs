using PondashiEX.Project;

namespace PondashiEX.Input;

public sealed class ShortcutDispatcher
{
    private readonly HashSet<Keys> _pressed = [];
    private readonly ShortcutManager _manager;
    private readonly Func<IEnumerable<SoundDefinition>> _sounds;
    private readonly Func<bool> _textEditing;
    private readonly Action<IReadOnlyList<ShortcutCommand>> _execute;

    public ShortcutDispatcher(
        ShortcutManager manager,
        Func<IEnumerable<SoundDefinition>> sounds,
        Func<bool> textEditing,
        Action<IReadOnlyList<ShortcutCommand>> execute)
    {
        _manager = manager;
        _sounds = sounds;
        _textEditing = textEditing;
        _execute = execute;
    }

    public void OnKeyChanged(Keys keyData, bool isDown)
    {
        var keyCode = keyData & Keys.KeyCode;
        if (!isDown)
        {
            _pressed.Remove(keyCode);
            return;
        }

        if (!_pressed.Add(keyCode))
        {
            return;
        }

        var commands = _manager.Resolve(keyData, _sounds(), _textEditing());
        if (commands.Count > 0)
        {
            _execute(commands);
        }
    }

    public void Reset() => _pressed.Clear();
}
