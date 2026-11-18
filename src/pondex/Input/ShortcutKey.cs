using System.ComponentModel;

namespace pondex.Input;

internal static class ShortcutKey
{
    private static readonly KeysConverter Converter = new();

    internal static object? Parse(string key, KeysConverter converter) => key.ToUpperInvariant() switch
    {
        "SPACE" => Keys.Space,
        "ENTER" or "RETURN" => Keys.Enter,
        var normalized => converter.ConvertFromInvariantString(normalized)
    };

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
            if (Parse(candidate, Converter) is not Keys keys)
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
