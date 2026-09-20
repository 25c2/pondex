namespace PondashiEX.Audio;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
    Fading
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

