namespace pondex.Input;

public enum ShortcutCommandType
{
    Play,
    PlayExclusive,
    Stop,
    StopAll
}

public sealed record ShortcutCommand(ShortcutCommandType Type, Guid? SoundId = null);

