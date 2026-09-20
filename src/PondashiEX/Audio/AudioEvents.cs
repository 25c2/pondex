namespace PondashiEX.Audio;

public sealed class SoundStateChangedEventArgs : EventArgs
{
    public SoundStateChangedEventArgs(Guid soundId, PlaybackState state)
    {
        SoundId = soundId;
        State = state;
    }

    public Guid SoundId { get; }

    public PlaybackState State { get; }
}

public sealed class SoundPlaybackEventArgs : EventArgs
{
    public SoundPlaybackEventArgs(Guid soundId, PlaybackEndReason? endReason = null)
    {
        SoundId = soundId;
        EndReason = endReason;
    }

    public Guid SoundId { get; }

    public PlaybackEndReason? EndReason { get; }
}

public sealed class SoundErrorEventArgs : EventArgs
{
    public SoundErrorEventArgs(Guid soundId, string message, Exception exception)
    {
        SoundId = soundId;
        Message = message;
        Exception = exception;
    }

    public Guid SoundId { get; }

    public string Message { get; }

    public Exception Exception { get; }
}

public sealed class OutputDeviceFallbackEventArgs : EventArgs
{
    public OutputDeviceFallbackEventArgs(
        int failedDeviceNumber,
        bool recovered,
        Exception exception)
    {
        FailedDeviceNumber = failedDeviceNumber;
        Recovered = recovered;
        Exception = exception;
    }

    public int FailedDeviceNumber { get; }

    public bool Recovered { get; }

    public Exception Exception { get; }
}
