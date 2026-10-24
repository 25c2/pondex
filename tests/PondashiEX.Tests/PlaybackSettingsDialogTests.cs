using PondashiEX.Project;
using PondashiEX.UI;

namespace PondashiEX.Tests;

public sealed class PlaybackSettingsDialogTests
{
    private static readonly int[] StandardPositions = [100, 100, 0];
    [Fact]
    public void SlidersReflectInitialValuesAndUpdateNumericInputs()
    {
        using var dialog = new PlaybackSettingsDialog(new SoundDefinition { Speed = 1.17F, Pitch = 0.89F, Pan = -0.43F });
        var sliders = Descendants(dialog).OfType<TrackBar>().ToArray();
        Assert.Equal(new[] { 117, 89, -43 }, sliders.Select(slider => slider.Value));
        var changes = 0;
        dialog.SettingsChanged += (_, _) => changes++;

        sliders[0].Value = 137;
        Assert.Equal(1.37F, dialog.Speed);
        sliders[1].Value = 175;
        Assert.Equal(1.75F, dialog.Pitch);
        sliders[2].Value = 62;
        Assert.Equal(0.62F, dialog.Pan);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void NumericInputsUpdateSlidersAndResetUpdatesEverythingOnce()
    {
        using var dialog = new PlaybackSettingsDialog(new SoundDefinition());
        var numeric = Descendants(dialog).OfType<NumericUpDown>().ToArray();
        var sliders = Descendants(dialog).OfType<TrackBar>().ToArray();
        numeric[0].Value = 0.51m;
        numeric[1].Value = 1.99m;
        numeric[2].Value = -100;
        Assert.Equal(new[] { 51, 199, -100 }, sliders.Select(slider => slider.Value));
        var changes = 0;
        dialog.SettingsChanged += (_, _) => changes++;

        dialog.ShowInTaskbar = false;
        dialog.Opacity = 0;
        dialog.Show();
        Descendants(dialog).OfType<Button>().Single(button => button.Text == "標準に戻す").PerformClick();

        Assert.Equal(1F, dialog.Speed);
        Assert.Equal(1F, dialog.Pitch);
        Assert.Equal(0F, dialog.Pan);
        Assert.Equal(StandardPositions, sliders.Select(slider => slider.Value));
        Assert.Equal(1, changes);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) { yield return descendant; }
        }
    }
}
