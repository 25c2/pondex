using System.Drawing;
using PondashiEX.Project;
using PondashiEX.UI;

namespace PondashiEX.Tests;

public sealed class SoundOrderingTests
{
    [Fact]
    public void ReorderSoundDefinitions_MovesSoundBeforeTarget()
    {
        var first = new SoundDefinition { Name = "1" };
        var second = new SoundDefinition { Name = "2" };
        var third = new SoundDefinition { Name = "3" };
        var sounds = new List<SoundDefinition> { first, second, third };

        var changed = MainForm.ReorderSoundDefinitions(
            sounds,
            third.Id,
            first.Id,
            insertAfter: false,
            out var newIndex);

        Assert.True(changed);
        Assert.Equal(0, newIndex);
        Assert.Equal([third, first, second], sounds);
    }

    [Fact]
    public void ReorderSoundDefinitions_MovesSoundAfterTarget()
    {
        var first = new SoundDefinition { Name = "1" };
        var second = new SoundDefinition { Name = "2" };
        var third = new SoundDefinition { Name = "3" };
        var sounds = new List<SoundDefinition> { first, second, third };

        var changed = MainForm.ReorderSoundDefinitions(
            sounds,
            first.Id,
            third.Id,
            insertAfter: true,
            out var newIndex);

        Assert.True(changed);
        Assert.Equal(2, newIndex);
        Assert.Equal([second, third, first], sounds);
    }

    [Fact]
    public void ReorderSoundDefinitions_AdjacentNoOpKeepsOrder()
    {
        var first = new SoundDefinition { Name = "1" };
        var second = new SoundDefinition { Name = "2" };
        var sounds = new List<SoundDefinition> { first, second };

        var changed = MainForm.ReorderSoundDefinitions(
            sounds,
            first.Id,
            second.Id,
            insertAfter: false,
            out var newIndex);

        Assert.False(changed);
        Assert.Equal(0, newIndex);
        Assert.Equal([first, second], sounds);
    }

    [Fact]
    public void FindSoundDropPlacement_UsesHorizontalPositionInsideSelectedRow()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var layouts = new[]
        {
            new SoundDropLayout(ids[0], new Rectangle(10, 10, 100, 80)),
            new SoundDropLayout(ids[1], new Rectangle(120, 10, 100, 80)),
            new SoundDropLayout(ids[2], new Rectangle(230, 10, 100, 80)),
            new SoundDropLayout(ids[3], new Rectangle(10, 100, 100, 80)),
            new SoundDropLayout(ids[4], new Rectangle(120, 100, 100, 80)),
            new SoundDropLayout(ids[5], new Rectangle(230, 100, 100, 80))
        };

        var placement = MainForm.FindSoundDropPlacement(
            layouts,
            ids[5],
            new Point(180, 120));

        Assert.Equal(new SoundDropPlacement(ids[4], InsertAfter: true), placement);
    }

    [Fact]
    public void FindSoundDropPlacement_DropInGapBeforeSecondRowUsesRowMajorOrder()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var layouts = new[]
        {
            new SoundDropLayout(ids[0], new Rectangle(10, 10, 100, 80)),
            new SoundDropLayout(ids[1], new Rectangle(120, 10, 100, 80)),
            new SoundDropLayout(ids[2], new Rectangle(230, 10, 100, 80)),
            new SoundDropLayout(ids[3], new Rectangle(10, 110, 100, 80)),
            new SoundDropLayout(ids[4], new Rectangle(120, 110, 100, 80)),
            new SoundDropLayout(ids[5], new Rectangle(230, 110, 100, 80))
        };

        var placement = MainForm.FindSoundDropPlacement(
            layouts,
            ids[0],
            new Point(5, 105));

        Assert.Equal(new SoundDropPlacement(ids[3], InsertAfter: false), placement);
    }

    [Fact]
    public void FindSoundDropPlacement_DropBelowAllRowsMovesToEnd()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var layouts = new[]
        {
            new SoundDropLayout(first, new Rectangle(10, 10, 100, 80)),
            new SoundDropLayout(second, new Rectangle(120, 10, 100, 80)),
            new SoundDropLayout(third, new Rectangle(10, 100, 100, 80))
        };

        var placement = MainForm.FindSoundDropPlacement(
            layouts,
            first,
            new Point(300, 300));

        Assert.Equal(new SoundDropPlacement(third, InsertAfter: true), placement);
    }

    [Fact]
    public void FindSoundDropPlacement_DroppingOnOnlyCardInLastRowKeepsItsPosition()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var fourth = Guid.NewGuid();
        var layouts = new[]
        {
            new SoundDropLayout(first, new Rectangle(10, 10, 100, 80)),
            new SoundDropLayout(second, new Rectangle(120, 10, 100, 80)),
            new SoundDropLayout(third, new Rectangle(230, 10, 100, 80)),
            new SoundDropLayout(fourth, new Rectangle(10, 100, 100, 80))
        };

        var placement = MainForm.FindSoundDropPlacement(
            layouts,
            fourth,
            new Point(60, 140));

        Assert.Equal(new SoundDropPlacement(third, InsertAfter: true), placement);
    }

    [Fact]
    public void RetargetGroupReferences_UpdatesShortcutsAndTriggersAndRemovesDuplicates()
    {
        var model = new ProjectModel
        {
            GroupShortcuts =
            [
                new GroupShortcutDefinition { Group = "SE", Key = "G" },
                new GroupShortcutDefinition { Group = "SFX", Key = "G" }
            ],
            Triggers =
            [
                new TriggerDefinition { TargetGroup = "se" }
            ]
        };

        var updated = MainForm.RetargetGroupReferences(model, "SE", "SFX");

        Assert.Equal(2, updated);
        var shortcut = Assert.Single(model.GroupShortcuts);
        Assert.Equal("SFX", shortcut.Group);
        Assert.Equal("SFX", Assert.Single(model.Triggers).TargetGroup);
    }

    [Fact]
    public void RemoveSoundDefinition_RemovesDirectAndOrphanedGroupReferences()
    {
        var sound = new SoundDefinition { Group = "SE" };
        var model = new ProjectModel
        {
            Sounds = [sound],
            GroupShortcuts = [new GroupShortcutDefinition { Group = "se", Key = "G" }],
            Triggers =
            [
                new TriggerDefinition { TargetSoundId = sound.Id, TargetGroup = "SE" },
                new TriggerDefinition { Parameters = new TriggerParameters { SourceSoundId = sound.Id } },
                new TriggerDefinition { TargetGroup = "SE" }
            ]
        };

        var references = MainForm.CountSoundRemovalReferences(model, sound);
        var result = MainForm.RemoveSoundDefinition(model, sound.Id);

        Assert.Equal(4, references);
        Assert.True(result.Removed);
        Assert.Equal(4, result.RelatedSettingsRemoved);
        Assert.Empty(model.Sounds);
        Assert.Empty(model.GroupShortcuts);
        Assert.Empty(model.Triggers);
    }

    [Fact]
    public void RemoveSoundDefinition_PreservesGroupReferencesWhileAnotherMemberExists()
    {
        var removed = new SoundDefinition { Group = "SE" };
        var remaining = new SoundDefinition { Group = "se" };
        var groupShortcut = new GroupShortcutDefinition { Group = "SE", Key = "G" };
        var groupTrigger = new TriggerDefinition { TargetGroup = "SE" };
        var model = new ProjectModel
        {
            Sounds = [removed, remaining],
            GroupShortcuts = [groupShortcut],
            Triggers = [groupTrigger]
        };

        var result = MainForm.RemoveSoundDefinition(model, removed.Id);

        Assert.True(result.Removed);
        Assert.Equal(0, result.RelatedSettingsRemoved);
        Assert.Equal(remaining, Assert.Single(model.Sounds));
        Assert.Equal(groupShortcut, Assert.Single(model.GroupShortcuts));
        Assert.Equal(groupTrigger, Assert.Single(model.Triggers));
    }

    [Fact]
    public void GroupShortcutsEqual_RequiresSameOrderAndValues()
    {
        var first = new[] { new GroupShortcutDefinition { Group = "SE", Key = "G" } };
        var same = new[] { new GroupShortcutDefinition { Group = "SE", Key = "G" } };
        var different = new[] { new GroupShortcutDefinition { Group = "SE", Key = "H" } };

        Assert.True(MainForm.GroupShortcutsEqual(first, same));
        Assert.False(MainForm.GroupShortcutsEqual(first, different));
    }

    [Fact]
    public void HasProjectSettingsChanges_ReturnsFalseForUnchangedValues()
    {
        var settings = new ProjectSettings
        {
            OutputDevice = "default",
            BackgroundInput = true,
            SilentLoop = true,
            SoundListColumns = 3
        };
        var project = new ProjectMetadata { Name = "本番", ThemeColor = "#202020" };

        var changed = MainForm.HasProjectSettingsChanges(
            settings,
            project,
            "default",
            backgroundInput: true,
            silentLoop: true,
            fadeOutOnStop: true,
            fadeOutOnStopAll: false,
            darkMode: false,
            soundListColumns: 3,
            "本番",
            "#202020");

        Assert.False(changed);
    }

    [Fact]
    public void HasProjectSettingsChanges_DetectsChangedValue()
    {
        var settings = new ProjectSettings { SoundListColumns = 1 };
        var project = new ProjectMetadata();

        var changed = MainForm.HasProjectSettingsChanges(
            settings,
            project,
            settings.OutputDevice,
            settings.BackgroundInput,
            settings.SilentLoop,
            settings.FadeOutOnStop,
            fadeOutOnStopAll: true,
            darkMode: true,
            soundListColumns: 2,
            project.Name,
            project.ThemeColor);

        Assert.True(changed);
    }
}
