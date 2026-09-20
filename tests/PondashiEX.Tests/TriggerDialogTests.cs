using PondashiEX.Project;
using PondashiEX.UI;

namespace PondashiEX.Tests;

public sealed class TriggerDialogTests
{
    [Fact]
    public void Constructor_WithNewTrigger_DoesNotThrow()
    {
        var sound = new SoundDefinition { Id = Guid.NewGuid(), Name = "効果音" };

        using var dialog = new TriggerDialog(
            new TriggerDefinition { TargetSoundId = sound.Id },
            [sound]);

        Assert.Equal("イベントトリガ", dialog.Text);
    }

    [Fact]
    public void Constructor_WithInvalidPersistedEnum_FallsBackWithoutCrashing()
    {
        var sound = new SoundDefinition { Id = Guid.NewGuid(), Name = "効果音" };
        var trigger = new TriggerDefinition
        {
            TargetSoundId = sound.Id,
            TriggerType = (TriggerType)999,
            Action = (TriggerAction)999
        };

        using var dialog = new TriggerDialog(trigger, [sound]);

        Assert.Equal("イベントトリガ", dialog.Text);
    }

    [Fact]
    public void Constructor_OffersDistinctGroupsAsActionTargets()
    {
        var sound = new SoundDefinition { Id = Guid.NewGuid(), Name = "効果音", Group = "SE" };
        var trigger = new TriggerDefinition
        {
            TargetGroup = "SE",
            Action = TriggerAction.Stop
        };

        using var dialog = new TriggerDialog(trigger, [sound]);

        var targetCombo = Descendants(dialog)
            .OfType<ComboBox>()
            .Single(combo => combo.Items.Count == 2);
        Assert.Equal("グループ: SE", targetCombo.GetItemText(targetCombo.SelectedItem));
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
