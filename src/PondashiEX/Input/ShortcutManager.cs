using PondashiEX.Project;

namespace PondashiEX.Input;

public sealed class ShortcutManager
{
    private readonly KeysConverter _converter = new();

    public IReadOnlyList<ShortcutCommand> Resolve(
        Keys keyData,
        IEnumerable<SoundDefinition> sounds,
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

        var stop = modifiers == Keys.Shift;
        if (modifiers != Keys.None && !stop)
        {
            return [];
        }

        return sounds
            .Where(sound => Matches(sound.Key, keyCode))
            .Select(sound => new ShortcutCommand(
                stop ? ShortcutCommandType.Stop : ShortcutCommandType.Play,
                sound.Id))
            .ToArray();
    }

    private bool Matches(string assignedKey, Keys pressedKey)
    {
        if (string.IsNullOrWhiteSpace(assignedKey))
        {
            return false;
        }

        try
        {
            var normalized = assignedKey.Trim().ToUpperInvariant();
            var converted = _converter.ConvertFromInvariantString(normalized);
            return converted is Keys key && (key & Keys.KeyCode) == pressedKey;
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
