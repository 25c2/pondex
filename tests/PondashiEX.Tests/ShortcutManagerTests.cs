using PondashiEX.Input;
using PondashiEX.Project;

namespace PondashiEX.Tests;

public sealed class ShortcutManagerTests
{
    private readonly ShortcutManager _manager = new();
    private readonly SoundDefinition _first = new() { Id = Guid.NewGuid(), Key = "A" };
    private readonly SoundDefinition _second = new() { Id = Guid.NewGuid(), Key = "A" };

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
}
