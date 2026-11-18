namespace pondex.Audio;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
    Fading,
    CheckingOutput,
    Preparing,
    Missing,
    Failed
}

public enum PlaybackEndReason
{
    Natural,
    Stopped,
    Replaced,
    StopAll,
    DeviceChanged,
    Error
}


internal enum PlaybackStartResult { Started, Cancelled, Missing, Failed }
