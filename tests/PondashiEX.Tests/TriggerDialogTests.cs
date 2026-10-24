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

    [Fact]
    public void MissingReferences_AreDisplayedWithoutSelectingAnotherSound()
    {
        var missingSource = Guid.NewGuid();
        var missingTarget = Guid.NewGuid();
        var trigger = new TriggerDefinition
        {
            TargetSoundId = missingTarget,
            TriggerType = TriggerType.SoundElapsed,
            Parameters = new TriggerParameters { SourceSoundId = missingSource }
        };
        using var dialog = new TriggerDialog(trigger, [new SoundDefinition { Name = "別の音源" }]);
        var combos = Descendants(dialog).OfType<ComboBox>().ToArray();
        Assert.Equal(2, combos.Count(combo => combo.GetItemText(combo.SelectedItem)?.Contains("不明な音源", StringComparison.Ordinal) == true));
        Assert.Equal(missingTarget, trigger.TargetSoundId);
        Assert.Equal(missingSource, trigger.Parameters.SourceSoundId);
    }

    [Fact]
    public void MissingGroup_IsDisplayedWithoutSelectingAnotherTarget()
    {
        var trigger = new TriggerDefinition { TargetGroup = "消えたグループ" };
        using var dialog = new TriggerDialog(trigger, [new SoundDefinition { Group = "別グループ" }]);
        Assert.Contains(Descendants(dialog).OfType<ComboBox>(), combo =>
            combo.GetItemText(combo.SelectedItem) == "不明なグループ: 消えたグループ");
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
