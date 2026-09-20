using PondashiEX.Project;

namespace PondashiEX.Audio;

public interface IAudioEngine : IDisposable
{
    event EventHandler<SoundStateChangedEventArgs>? StateChanged;

    event EventHandler<SoundPlaybackEventArgs>? PlaybackStarted;

    event EventHandler<SoundPlaybackEventArgs>? PlaybackEnded;

    event EventHandler<SoundErrorEventArgs>? PlaybackFailed;

    event EventHandler<OutputDeviceFallbackEventArgs>? OutputDeviceFallback;

    float MasterVolume { get; set; }

    bool MasterMute { get; set; }

    bool SilentLoopEnabled { get; set; }

    int OutputDeviceNumber { get; }

    Task PlayAsync(SoundDefinition sound, string resolvedPath, CancellationToken cancellationToken = default);

    void Stop(Guid soundId);

    void Pause(Guid soundId);

    void Resume(Guid soundId);

    void FadeVolume(Guid soundId, float targetVolume, TimeSpan duration);

    void StopAll();

    PlaybackState GetState(Guid soundId);

    IReadOnlyCollection<Guid> GetActiveSoundIds();

    void ApplyLiveSettings(SoundDefinition sound);

    void SelectOutputDevice(int deviceNumber);
}
