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
    public async Task ApplySoundOrder_PreservesDefinitionsAndPersistsOrder()
    {
        using var directory = new TestDirectory();
        var first = new SoundDefinition { Name = "同名", Group = "SE", Key = "A", Memo = "最初の音源" };
        var second = new SoundDefinition { Name = "同名", Group = "BGM", Key = "B" };
        var model = new ProjectModel { Sounds = [first, second] };
        var originalList = model.Sounds;
        var trigger = new TriggerDefinition { TargetSoundId = first.Id };
        model.Triggers.Add(trigger);

        Assert.True(MainForm.ApplySoundOrder(model.Sounds, [second.Id, first.Id]));

        Assert.Same(originalList, model.Sounds);
        Assert.Same(second, model.Sounds[0]);
        Assert.Same(first, model.Sounds[1]);
        Assert.Equal(first.Id, trigger.TargetSoundId);
        var path = directory.GetPath("order.pondex");
        var serializer = new ProjectSerializer();
        await serializer.SaveAsync(path, model);
        var restored = await serializer.LoadAsync(path);
        Assert.Equal(new[] { second.Id, first.Id }, restored.Sounds.Select(sound => sound.Id));
        Assert.Equal("最初の音源", restored.Sounds[1].Memo);
    }

    [Fact]
    public void ApplySoundOrder_UnchangedOrInvalidOrdersDoNotMutateModel()
    {
        var first = new SoundDefinition();
        var second = new SoundDefinition();
        var sounds = new List<SoundDefinition> { first, second };
        Assert.False(MainForm.ApplySoundOrder(sounds, [first.Id, second.Id]));
        Assert.False(MainForm.ApplySoundOrder(sounds, [first.Id]));
        Assert.False(MainForm.ApplySoundOrder(sounds, [first.Id, first.Id]));
        Assert.False(MainForm.ApplySoundOrder(sounds, [Guid.NewGuid(), second.Id]));
        Assert.Equal([first, second], sounds);
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
