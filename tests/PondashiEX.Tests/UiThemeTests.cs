using System.Drawing;
using PondashiEX.Project;
using PondashiEX.UI;

namespace PondashiEX.Tests;

[Collection(WinFormsTestGroup.Name)]
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
        card.EnsureDetailsCreated();
        card.ApplyDarkMode(true);

        card.ApplyDarkMode(false);

        Assert.True(card.BackColor.GetBrightness() > 0.75f);
        Assert.Contains(
            Descendants(card).OfType<TextBox>(),
            textBox => textBox.BackColor == SystemColors.Window);
    }

    [Fact]
    public void DarkButtonsUseSubtleBordersAndCanReturnToStandardStyle()
    {
        using var dialog = new PlaybackSettingsDialog(new SoundDefinition());
        UiTheme.Apply(dialog, true);
        var buttons = Descendants(dialog).OfType<Button>().ToArray();
        Assert.All(buttons, button =>
        {
            Assert.Equal(FlatStyle.Flat, button.FlatStyle);
            Assert.True(button.FlatAppearance.BorderColor.GetBrightness() < 0.4F);
            Assert.True(button.ForeColor.GetBrightness() > 0.65F);
        });

        UiTheme.Apply(dialog, false);

        Assert.All(buttons, button => Assert.Equal(FlatStyle.Standard, button.FlatStyle));
    }

    [Fact]
    public void TabStripPaintsUnusedAreaDarkAndPreservesSelection()
    {
        using var form = new Form { ClientSize = new Size(600, 260), ShowInTaskbar = false, Opacity = 0 };
        using var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("音源"));
        tabs.TabPages.Add(new TabPage("イベントトリガ"));
        form.Controls.Add(tabs);
        UiTheme.Apply(form, true);
        form.Show();
        using var bitmap = new Bitmap(tabs.Width, tabs.Height);
        tabs.DrawToBitmap(bitmap, tabs.ClientRectangle);
        var unusedStrip = bitmap.GetPixel(tabs.Width - 12, tabs.GetTabRect(0).Top + 5);
        Assert.True(unusedStrip.GetBrightness() < 0.35F, $"未使用タブ領域: {unusedStrip}");

        tabs.SelectedIndex = 1;
        UiTheme.Apply(form, false);
        tabs.DrawToBitmap(bitmap, tabs.ClientRectangle);
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(tabs.Width - 12, tabs.GetTabRect(0).Top + 5).ToArgb());
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
