using System.Drawing;
using PondashiEX.Project;
using PondashiEX.UI;

namespace PondashiEX.Tests;

public sealed class UiThemeTests
{
    [Fact]
    public void SoundCard_DarkModeUsesDarkSurfaceAndReadableStateText()
    {
        using var card = new SoundCard(new SoundDefinition());

        card.ApplyDarkMode(true);

        var state = Descendants(card)
            .OfType<Label>()
            .Single(label => label.Text == "停止中");
        Assert.True(card.BackColor.GetBrightness() < 0.35f);
        Assert.True(state.BackColor.GetBrightness() < 0.35f);
        Assert.True(state.ForeColor.GetBrightness() > 0.65f);
    }

    [Fact]
    public void SoundCard_CanReturnFromDarkModeToLightMode()
    {
        using var card = new SoundCard(new SoundDefinition());
        card.ApplyDarkMode(true);

        card.ApplyDarkMode(false);

        Assert.True(card.BackColor.GetBrightness() > 0.75f);
        Assert.Contains(
            Descendants(card).OfType<TextBox>(),
            textBox => textBox.BackColor == SystemColors.Window);
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
