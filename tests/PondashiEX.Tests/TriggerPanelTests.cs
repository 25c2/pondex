using System.Reflection;
using PondashiEX.Project;
using PondashiEX.UI;

namespace PondashiEX.Tests;

[Collection(WinFormsTestGroup.Name)]
public sealed class TriggerPanelTests
{
    [Fact]
    public void EnabledCheckbox_UpdatesDefinitionDirectly()
    {
        var sound = new SoundDefinition { Id = Guid.NewGuid(), Name = "ベル" };
        var trigger = new TriggerDefinition
        {
            Enabled = true,
            TargetSoundId = sound.Id,
            TriggerType = TriggerType.ClockTime,
            Parameters = new TriggerParameters { ClockTime = new TimeOnly(12, 0) }
        };
        using var form = new Form();
        using var panel = new TriggerPanel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        panel.SetData([trigger], [sound]);
        form.Show();
        var grid = GetGrid(panel);
        var changes = 0;
        panel.DefinitionsChanged += (_, _) => changes++;

        grid.Rows[0].Cells[0].Value = false;
        grid.EndEdit();
        Application.DoEvents();

        Assert.False(trigger.Enabled);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Grid_ShowsLocalizedConditionAndGroupTarget()
    {
        var sound = new SoundDefinition { Id = Guid.NewGuid(), Name = "ベル", Group = "SE" };
        var trigger = new TriggerDefinition
        {
            TargetGroup = "SE",
            Action = TriggerAction.Stop,
            TriggerType = TriggerType.SoundEnded,
            Parameters = new TriggerParameters { SourceSoundId = sound.Id }
        };
        using var panel = new TriggerPanel();

        panel.SetData([trigger], [sound]);

        var grid = GetGrid(panel);
        Assert.Equal("ベル の再生終了", grid.Rows[0].Cells[2].Value);
        Assert.Equal("停止", grid.Rows[0].Cells[3].Value);
        Assert.Equal("グループ: SE", grid.Rows[0].Cells[4].Value);
    }

    [Fact]
    public void MaximumCount_DisablesAndGuardsAdd()
    {
        var sound = new SoundDefinition();
        var definitions = Enumerable.Range(0, ProjectLimits.MaxTriggers)
            .Select(_ => new TriggerDefinition { TargetSoundId = sound.Id }).ToList();
        using var panel = new TriggerPanel();
        panel.SetData(definitions, [sound]);
        var toolbar = panel.Controls[0].Controls.OfType<FlowLayoutPanel>().Single();
        Assert.False(toolbar.Controls.OfType<Button>().Single(button => button.Text == "＋ 追加").Enabled);
        typeof(TriggerPanel).GetMethod("AddTrigger", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null);
        Assert.Equal(ProjectLimits.MaxTriggers, definitions.Count);
    }

    private static DataGridView GetGrid(TriggerPanel panel) =>
        (DataGridView)(typeof(TriggerPanel).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(panel)
            ?? throw new InvalidOperationException("TriggerPanelの一覧が見つかりません。"));
}
