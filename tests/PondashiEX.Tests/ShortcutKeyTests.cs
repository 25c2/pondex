using PondashiEX.Input;

namespace PondashiEX.Tests;

public sealed class ShortcutKeyTests
{
    [Theory]
    [InlineData("a", "A")]
    [InlineData(" 2 ", "2")]
    [InlineData("f1", "F1")]
    public void TryNormalize_AcceptsSingleShortcutKey(string input, string expected)
    {
        var valid = ShortcutKey.TryNormalize(input, out var normalized);

        Assert.True(valid);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("い")]
    [InlineData("Control+A")]
    [InlineData("A2")]
    public void TryNormalize_RejectsUnsupportedKeyName(string input)
    {
        var valid = ShortcutKey.TryNormalize(input, out var normalized);

        Assert.False(valid);
        Assert.Empty(normalized);
    }
}
