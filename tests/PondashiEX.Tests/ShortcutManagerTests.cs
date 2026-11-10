using PondashiEX.Input;
using PondashiEX.Project;

namespace PondashiEX.Tests;

public sealed class ShortcutManagerTests
{
    private readonly ShortcutManager _manager = new();
    private readonly SoundDefinition _first = new() { Id = Guid.NewGuid(), Key = "A" };
    private readonly SoundDefinition _second = new() { Id = Guid.NewGuid(), Key = "A" };

    [Fact]
    public void AltAssignedKey_ResolvesExclusiveSoundWithoutChangingNormalGroupPlay()
    {
        var target = new SoundDefinition { Key = "A", Group = "BGM" };
        var other = new SoundDefinition { Key = "B", Group = "BGM" };
        var groups = new[] { new GroupShortcutDefinition { Group = "BGM", Key = "G" } };
        var manager = new ShortcutManager();
        var command = Assert.Single(manager.Resolve(Keys.Alt | Keys.A, [target, other], groups, false));
        Assert.Equal(ShortcutCommandType.PlayExclusive, command.Type);
        Assert.Equal(target.Id, command.SoundId);
        Assert.Equal(2, manager.Resolve(Keys.G, [target, other], groups, false).Count);
        Assert.Empty(manager.Resolve(Keys.Alt | Keys.G, [target, other], groups, false));
    }

    [Fact]
    public void ExclusiveCollision_IsRejectedWhilePlainAndShiftRemainBatched()
    {
        var first = new SoundDefinition { Key = "A", Group = " BGM " };
        var second = new SoundDefinition { Key = "A", Group = "bgm" };
        var group = new[] { new GroupShortcutDefinition { Key = "A", Group = "BGM" } };
        Assert.Empty(_manager.Resolve(Keys.Alt | Keys.A, [first, second], group, false));
        Assert.Equal(2, _manager.Resolve(Keys.A, [first, second], group, false).Count);
        Assert.Equal(2, _manager.Resolve(Keys.Shift | Keys.A, [first, second], group, false).Count);
        second.Key = "B";
        Assert.Equal(first.Id, Assert.Single(_manager.Resolve(Keys.Alt | Keys.A, [first, second], group, false)).SoundId);
    }

    [Fact]
    public void UngroupedExclusiveCollision_IsRejectedWithoutChangingPlainMultiPlay()
    {
        var first = new SoundDefinition { Key = "A" };
        var second = new SoundDefinition { Key = "A", Group = " " };
        Assert.Empty(_manager.Resolve(Keys.Alt | Keys.A, [first, second], false));
        Assert.Equal(2, _manager.Resolve(Keys.A, [first, second], false).Count);
    }

    [Fact]
    public void PlainKey_PlaysAllSoundsAssignedToSameKey()
    {
        var commands = _manager.Resolve(Keys.A, [_first, _second], textEditing: false);

        Assert.Equal(2, commands.Count);
        Assert.All(commands, command => Assert.Equal(ShortcutCommandType.Play, command.Type));
        Assert.Equal([_first.Id, _second.Id], commands.Select(command => command.SoundId));
    }

    [Fact]
    public void ShiftKey_StopsAllSoundsAssignedToSameKey()
    {
        var commands = _manager.Resolve(Keys.Shift | Keys.A, [_first, _second], textEditing: false);

        Assert.Equal(2, commands.Count);
        Assert.All(commands, command => Assert.Equal(ShortcutCommandType.Stop, command.Type));
    }

    [Fact]
    public void TextEditing_SuppressesCharacterCueButNotEmergencyStopAll()
    {
        Assert.Empty(_manager.Resolve(Keys.A, [_first], textEditing: true));

        var command = Assert.Single(_manager.Resolve(Keys.Control | Keys.R, [_first], textEditing: true));
        Assert.Equal(ShortcutCommandType.StopAll, command.Type);
    }

    [Fact]
    public void LowercaseAssignedKey_MatchesWithoutCrashing()
    {
        _first.Key = "a";

        var command = Assert.Single(_manager.Resolve(Keys.A, [_first], textEditing: false));

        Assert.Equal(ShortcutCommandType.Play, command.Type);
    }

    [Fact]
    public void InvalidAssignedKey_IsIgnoredWithoutCrashing()
    {
        _first.Key = "い";

        var commands = _manager.Resolve(Keys.A, [_first], textEditing: false);

        Assert.Empty(commands);
    }

    [Fact]
    public void NumberKeyName_MatchesNumberKey()
    {
        _first.Key = "2";

        var command = Assert.Single(_manager.Resolve(Keys.D2, [_first], textEditing: false));

        Assert.Equal(_first.Id, command.SoundId);
    }

    [Fact]
    public void GroupShortcut_PlaysEverySoundInGroup()
    {
        _first.Group = "SE";
        _first.Key = string.Empty;
        _second.Group = "SE";
        _second.Key = string.Empty;
        var groupShortcuts = new[] { new GroupShortcutDefinition { Group = "se", Key = "G" } };

        var commands = _manager.Resolve(Keys.G, [_first, _second], groupShortcuts, textEditing: false);

        Assert.Equal([_first.Id, _second.Id], commands.Select(command => command.SoundId));
        Assert.All(commands, command => Assert.Equal(ShortcutCommandType.Play, command.Type));
    }

    [Fact]
    public void ShiftGroupShortcut_StopsEverySoundInGroup()
    {
        _first.Group = "BGM";
        _first.Key = string.Empty;
        _second.Group = "BGM";
        _second.Key = string.Empty;
        var groupShortcuts = new[] { new GroupShortcutDefinition { Group = "BGM", Key = "F2" } };

        var commands = _manager.Resolve(Keys.Shift | Keys.F2, [_first, _second], groupShortcuts, textEditing: false);

        Assert.Equal(2, commands.Count);
        Assert.All(commands, command => Assert.Equal(ShortcutCommandType.Stop, command.Type));
    }

    [Fact]
    public void IndividualAndGroupShortcut_DoesNotExecuteSameSoundTwice()
    {
        _first.Group = "SE";
        _first.Key = "A";
        var groupShortcuts = new[] { new GroupShortcutDefinition { Group = "SE", Key = "A" } };

        var commands = _manager.Resolve(Keys.A, [_first], groupShortcuts, textEditing: false);

        Assert.Single(commands);
    }
}
