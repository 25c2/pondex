using PondashiEX.Project;

namespace PondashiEX.Input;

public sealed class ShortcutDispatcher
{
    private readonly HashSet<Keys> _pressed = [];
    private readonly ShortcutManager _manager;
    private readonly Func<IEnumerable<SoundDefinition>> _sounds;
    private readonly Func<IEnumerable<GroupShortcutDefinition>> _groupShortcuts;
    private readonly Func<bool> _textEditing;
    private readonly Action<IReadOnlyList<ShortcutCommand>> _execute;
    private Dictionary<Keys, Guid[]> _soundsByKey = [];

    public ShortcutDispatcher(
        ShortcutManager manager,
        Func<IEnumerable<SoundDefinition>> sounds,
        Func<bool> textEditing,
        Action<IReadOnlyList<ShortcutCommand>> execute)
        : this(manager, sounds, () => [], textEditing, execute)
    {
    }

    public ShortcutDispatcher(
        ShortcutManager manager,
        Func<IEnumerable<SoundDefinition>> sounds,
        Func<IEnumerable<GroupShortcutDefinition>> groupShortcuts,
        Func<bool> textEditing,
        Action<IReadOnlyList<ShortcutCommand>> execute)
    {
        _manager = manager;
        _sounds = sounds;
        _groupShortcuts = groupShortcuts;
        _textEditing = textEditing;
        _execute = execute;
        RefreshBindings();
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

        var modifiers = keyData & Keys.Modifiers;
        if (keyCode == Keys.R && modifiers == Keys.Control)
        {
            _execute([new ShortcutCommand(ShortcutCommandType.StopAll)]);
            return;
        }

        if (_textEditing()
            || keyCode is Keys.None or Keys.ShiftKey or Keys.ControlKey or Keys.Menu)
        {
            return;
        }

        var stop = modifiers == Keys.Shift;
        if ((modifiers != Keys.None && !stop) || !_soundsByKey.TryGetValue(keyCode, out var soundIds))
        {
            return;
        }

        var commandType = stop ? ShortcutCommandType.Stop : ShortcutCommandType.Play;
        _execute(soundIds.Select(soundId => new ShortcutCommand(commandType, soundId)).ToArray());
    }

    public void Reset() => _pressed.Clear();

    public void RefreshBindings()
    {
        var sounds = _sounds().ToArray();
        var groupsByKey = new Dictionary<Keys, HashSet<string>>();
        foreach (var shortcut in _groupShortcuts())
        {
            var group = shortcut.Group?.Trim() ?? string.Empty;
            if (group.Length == 0 || !_manager.TryParseKey(shortcut.Key, out var keyCode))
            {
                continue;
            }

            if (!groupsByKey.TryGetValue(keyCode, out var groups))
            {
                groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                groupsByKey.Add(keyCode, groups);
            }

            groups.Add(group);
        }

        var soundIdsByKey = new Dictionary<Keys, HashSet<Guid>>();
        foreach (var sound in sounds)
        {
            if (_manager.TryParseKey(sound.Key, out var directKey))
            {
                AddBinding(soundIdsByKey, directKey, sound.Id);
            }

            var group = sound.Group?.Trim() ?? string.Empty;
            if (group.Length == 0)
            {
                continue;
            }

            foreach (var (groupKey, groups) in groupsByKey)
            {
                if (groups.Contains(group))
                {
                    AddBinding(soundIdsByKey, groupKey, sound.Id);
                }
            }
        }

        _soundsByKey = soundIdsByKey.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray());
    }

    private static void AddBinding(Dictionary<Keys, HashSet<Guid>> bindings, Keys key, Guid soundId)
    {
        if (!bindings.TryGetValue(key, out var soundIds))
        {
            soundIds = [];
            bindings.Add(key, soundIds);
        }

        soundIds.Add(soundId);
    }
}
