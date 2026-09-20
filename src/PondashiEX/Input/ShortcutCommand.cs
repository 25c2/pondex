namespace PondashiEX.Input;

public enum ShortcutCommandType
{
    Play,
    Stop,
    StopAll
}

public sealed record ShortcutCommand(ShortcutCommandType Type, Guid? SoundId = null);

