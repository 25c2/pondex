using PondashiEX.Input;
using PondashiEX.Project;

namespace PondashiEX.Tests;

public sealed class ShortcutDispatcherTests
{
    [Fact]
    public void OnKeyChanged_DeliversSameKeyCommandsAsOneBatch()
    {
        var first = new SoundDefinition { Id = Guid.NewGuid(), Key = "A", Group = "SE" };
        var second = new SoundDefinition { Id = Guid.NewGuid(), Key = "A", Group = "SE" };
        IReadOnlyList<ShortcutCommand>? executed = null;
        var dispatcher = new ShortcutDispatcher(
            new ShortcutManager(),
            () => [first, second],
            () => false,
            commands => executed = commands);

        dispatcher.OnKeyChanged(Keys.A, isDown: true);

        Assert.NotNull(executed);
        Assert.Equal([first.Id, second.Id], executed.Select(command => command.SoundId));
    }

    [Fact]
    public void OnKeyChanged_KeyRepeatDoesNotExecuteBatchTwiceUntilReleased()
    {
        var sound = new SoundDefinition { Id = Guid.NewGuid(), Key = "A" };
        var executions = 0;
        var dispatcher = new ShortcutDispatcher(
            new ShortcutManager(),
            () => [sound],
            () => false,
            _ => executions++);

        dispatcher.OnKeyChanged(Keys.A, isDown: true);
        dispatcher.OnKeyChanged(Keys.A, isDown: true);
        dispatcher.OnKeyChanged(Keys.A, isDown: false);
        dispatcher.OnKeyChanged(Keys.A, isDown: true);

        Assert.Equal(2, executions);
    }

    [Fact]
    public void OnKeyChanged_ResolvesCurrentGroupShortcuts()
    {
        var first = new SoundDefinition { Id = Guid.NewGuid(), Group = "SE" };
        var second = new SoundDefinition { Id = Guid.NewGuid(), Group = "SE" };
        var shortcuts = new List<GroupShortcutDefinition>
        {
            new() { Group = "SE", Key = "G" }
        };
        IReadOnlyList<ShortcutCommand>? executed = null;
        var dispatcher = new ShortcutDispatcher(
            new ShortcutManager(),
            () => [first, second],
            () => shortcuts,
            () => false,
            commands => executed = commands);

        dispatcher.OnKeyChanged(Keys.G, isDown: true);

        Assert.NotNull(executed);
        Assert.Equal([first.Id, second.Id], executed.Select(command => command.SoundId));
    }

    [Fact]
    public void RefreshBindings_AppliesChangedAssignments()
    {
        var sound = new SoundDefinition { Id = Guid.NewGuid(), Key = "A" };
        IReadOnlyList<ShortcutCommand>? executed = null;
        var dispatcher = new ShortcutDispatcher(
            new ShortcutManager(),
            () => [sound],
            () => false,
            commands => executed = commands);
        sound.Key = "B";

        dispatcher.RefreshBindings();
        dispatcher.OnKeyChanged(Keys.B, isDown: true);

        Assert.Equal(sound.Id, Assert.Single(executed!).SoundId);
    }
}
