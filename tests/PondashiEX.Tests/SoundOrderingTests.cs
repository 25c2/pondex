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
}
