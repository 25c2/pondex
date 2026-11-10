using pondex.Project;

namespace pondex.Input;

public sealed class ShortcutManager
{
    private readonly KeysConverter _converter = new();

    public IReadOnlyList<ShortcutCommand> Resolve(
        Keys keyData,
        IEnumerable<SoundDefinition> sounds,
        bool textEditing) => Resolve(keyData, sounds, [], textEditing);

    public IReadOnlyList<ShortcutCommand> Resolve(
        Keys keyData,
        IEnumerable<SoundDefinition> sounds,
        IEnumerable<GroupShortcutDefinition> groupShortcuts,
        bool textEditing)
    {
        var keyCode = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;

        if (keyCode == Keys.R && modifiers == Keys.Control)
        {
            return [new ShortcutCommand(ShortcutCommandType.StopAll)];
        }

        if (textEditing || keyCode is Keys.None or Keys.ShiftKey or Keys.ControlKey or Keys.Menu)
        {
            return [];
        }

        var exclusive = modifiers == Keys.Alt;
        var stop = modifiers == Keys.Shift;
        if (modifiers != Keys.None && !stop && !exclusive)
        {
            return [];
        }

        var matchingGroups = groupShortcuts
            .Where(shortcut => Matches(shortcut.Key, keyCode))
            .Select(shortcut => shortcut.Group?.Trim() ?? string.Empty)
            .Where(group => group.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targets = sounds.ToArray();
        if (exclusive && targets.Where(sound => Matches(sound.Key, keyCode))
            .GroupBy(sound => sound.Group?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            return [];
        }

        return targets
            .Where(sound => Matches(sound.Key, keyCode)
                || (!exclusive && matchingGroups.Contains(sound.Group?.Trim() ?? string.Empty)))
            .DistinctBy(sound => sound.Id)
            .Select(sound => new ShortcutCommand(
                stop ? ShortcutCommandType.Stop : exclusive ? ShortcutCommandType.PlayExclusive : ShortcutCommandType.Play,
                sound.Id))
            .ToArray();
    }

    private bool Matches(string assignedKey, Keys pressedKey)
    {
        return TryParseKey(assignedKey, out var assignedKeyCode) && assignedKeyCode == pressedKey;
    }

    public bool TryParseKey(string assignedKey, out Keys keyCode)
    {
        keyCode = Keys.None;
        if (string.IsNullOrWhiteSpace(assignedKey))
        {
            return false;
        }

        try
        {
            var normalized = assignedKey.Trim().ToUpperInvariant();
            var converted = _converter.ConvertFromInvariantString(normalized);
            if (converted is not Keys key)
            {
                return false;
            }

            keyCode = key & Keys.KeyCode;
            return keyCode != Keys.None;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
