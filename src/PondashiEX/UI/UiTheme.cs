namespace PondashiEX.UI;

internal static class UiTheme
{
    private const string FontFamilyName = "Meiryo UI";

    public static Font CreateFont(float size, FontStyle style = FontStyle.Regular) =>
        new(FontFamilyName, size, style, GraphicsUnit.Point);
}
