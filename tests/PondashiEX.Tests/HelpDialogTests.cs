using PondashiEX.UI;

namespace PondashiEX.Tests;

public sealed class HelpDialogTests
{
    [Fact]
    public void Constructor_IncludesFadeAndLoopGuidance()
    {
        using var dialog = new HelpDialog();
        var helpText = Descendants(dialog).OfType<RichTextBox>().Single().Text;

        Assert.Contains("Fade In／Fade Out", helpText, StringComparison.Ordinal);
        Assert.Contains("ループ開始（秒）", helpText, StringComparison.Ordinal);
        Assert.Contains("イベントトリガ", helpText, StringComparison.Ordinal);
        Assert.Contains(Descendants(dialog).OfType<Panel>(), panel => panel.Padding.Left >= 20);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
