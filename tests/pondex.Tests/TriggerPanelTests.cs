using System.Drawing;
using System.Reflection;
using pondex.Project;
using pondex.UI;

namespace pondex.Tests;

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

    [Fact]
    public void BulkButtons_UpdateEligibleTriggersOnceAndRespectEditingLock() => WinFormsTest.Run(() =>
    {
        var sound = new SoundDefinition { Group = "SE" };
        var clock = new TriggerDefinition
        {
            Enabled = false,
            TargetSoundId = sound.Id,
            TriggerType = TriggerType.ClockTime,
            Parameters = new TriggerParameters { ClockTime = new TimeOnly(12, 0) }
        };
        var group = new TriggerDefinition
        {
            Enabled = false,
            TargetGroup = "se",
            TriggerType = TriggerType.SoundElapsed,
            Parameters = new TriggerParameters { Elapsed = TimeSpan.Zero, SourceSoundId = sound.Id }
        };
        var held = new TriggerDefinition { Enabled = false, TargetGroup = "SE", TargetGroupOnHold = true };
        var missingGroup = new TriggerDefinition
        {
            Enabled = false,
            TargetGroup = "missing",
            TriggerType = TriggerType.StopwatchElapsed,
            Parameters = new TriggerParameters { Elapsed = TimeSpan.Zero }
        };
        var missingSource = new TriggerDefinition
        {
            Enabled = false,
            TargetSoundId = sound.Id,
            TriggerType = TriggerType.SoundEnded,
            Parameters = new TriggerParameters { SourceSoundId = Guid.NewGuid() }
        };
        var missingTime = new TriggerDefinition { Enabled = false, TargetSoundId = sound.Id, TriggerType = TriggerType.ClockTime };
        List<TriggerDefinition> triggers = [clock, group, held, missingGroup, missingSource, missingTime];
        using var form = new Form { ClientSize = new Size(440, 300) };
        using var panel = new TriggerPanel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        panel.SetData(triggers, [sound]);
        form.Show();
        Application.DoEvents();

        var toolbar = panel.Controls[0].Controls.OfType<FlowLayoutPanel>().Single();
        var allOn = toolbar.Controls.OfType<Button>().Single(button => button.Text == "全ON");
        var allOff = toolbar.Controls.OfType<Button>().Single(button => button.Text == "全OFF");
        Assert.All(toolbar.Controls.OfType<Button>(), button => Assert.True(toolbar.ClientRectangle.Contains(button.Bounds)));

        var changes = 0;
        var lastMessage = string.Empty;
        var areAnySkipped = false;
        panel.DefinitionsChanged += (_, _) => changes++;
        panel.BulkChangeCompleted += (message, skipped) => { lastMessage = message; areAnySkipped = skipped; };
        allOn.PerformClick();
        Assert.True(clock.Enabled);
        Assert.True(group.Enabled);
        Assert.All(triggers.Skip(2), item => Assert.False(item.Enabled));
        Assert.True(areAnySkipped);
        Assert.Contains("4件", lastMessage);
        Assert.Equal(1, changes);
        allOn.PerformClick();
        Assert.Equal(1, changes);

        allOff.PerformClick();
        Assert.All(triggers, item => Assert.False(item.Enabled));
        Assert.False(areAnySkipped);
        Assert.Equal(2, changes);

        panel.SetEditingLocked(true);
        Assert.False(allOn.Enabled);
        Assert.False(allOff.Enabled);
        allOn.PerformClick();
        Assert.All(triggers, item => Assert.False(item.Enabled));
        Assert.Equal(2, changes);
    });

    private static DataGridView GetGrid(TriggerPanel panel) =>
        (DataGridView)(typeof(TriggerPanel).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(panel)
            ?? throw new InvalidOperationException("TriggerPanelの一覧が見つかりません。"));
}
