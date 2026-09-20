using System.ComponentModel;

namespace PondashiEX.Input;

internal static class ShortcutKey
{
    private static readonly KeysConverter Converter = new();

    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        var candidate = new string((input ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character))
            .ToArray());
        if (candidate.Length == 0)
        {
            return false;
        }

        try
        {
            if (Converter.ConvertFromInvariantString(candidate.ToUpperInvariant()) is not Keys keys)
            {
                return false;
            }

            var keyCode = keys & Keys.KeyCode;
            if ((keys & Keys.Modifiers) != Keys.None
                || keyCode is Keys.None or Keys.ShiftKey or Keys.ControlKey or Keys.Menu)
            {
                return false;
            }

            normalized = Converter.ConvertToInvariantString(keyCode) ?? keyCode.ToString();
            return normalized.Length > 0;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or FormatException)
        {
            return false;
        }
    }
}
