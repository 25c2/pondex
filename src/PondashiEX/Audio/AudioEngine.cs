using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PondashiEX.Project;
using PondashiEX.Utility;

namespace PondashiEX.Audio;

public sealed class AudioEngine : IAudioEngine
{
    private static readonly WaveFormat MixerFormat = WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2);
    private readonly object _sync = new();
    private readonly Dictionary<Guid, SoundInstance> _instances = [];
    private readonly Dictionary<Guid, long> _soundOperationVersions = [];
    private readonly IAppLogger _logger;
    private readonly Func<int, ISampleProvider, IAudioOutput> _outputFactory;
    private readonly IFadePolicy _fadePolicy;
    private MixingSampleProvider _mixer;
    private VolumeSampleProvider _masterVolumeProvider;
    private IAudioOutput? _output;
    private int _deviceNumber = -1;
    private float _masterVolume = 1.0f;
    private bool _masterMute;
    private bool _fadeOutOnStopEnabled = true;
    private bool _silentLoopEnabled;
    private bool _disposed;
    private int _outputRecoveryQueued;
    private long _outputSamplesRead;

    public AudioEngine(IAppLogger logger)
        : this(
            logger,
            static (deviceNumber, provider) => new WaveOutAudioOutput(deviceNumber, provider),
            new SecondsFadePolicy())
    {
    }

    internal AudioEngine(
        IAppLogger logger,
        Func<int, ISampleProvider, IAudioOutput> outputFactory,
        IFadePolicy? fadePolicy = null)
    {
        _logger = logger;
        _outputFactory = outputFactory;
        _fadePolicy = fadePolicy ?? new SecondsFadePolicy();
        (_mixer, _masterVolumeProvider) = CreateMixer();
    }

    public event EventHandler<SoundStateChangedEventArgs>? StateChanged;

    public event EventHandler<SoundPlaybackEventArgs>? PlaybackStarted;

    public event EventHandler<SoundPlaybackEventArgs>? PlaybackEnded;

    public event EventHandler<SoundErrorEventArgs>? PlaybackFailed;

    public event EventHandler<OutputDeviceFallbackEventArgs>? OutputDeviceFallback;

    public float MasterVolume
    {
        get
        {
            lock (_sync)
            {
                return _masterVolume;
            }
        }
        set
        {
            lock (_sync)
            {
                _masterVolume = Math.Clamp(value, 0.0f, 1.0f);
                ApplyMasterVolume();
            }
        }
    }

    public bool MasterMute
    {
        get
        {
            lock (_sync)
            {
                return _masterMute;
            }
        }
        set
        {
            lock (_sync)
            {
                _masterMute = value;
                ApplyMasterVolume();
            }
        }
    }

    public bool FadeOutOnStopEnabled
    {
        get
        {
            lock (_sync)
            {
                return _fadeOutOnStopEnabled;
            }
        }
        set
        {
            lock (_sync)
            {
                _fadeOutOnStopEnabled = value;
            }
        }
    }

    public bool SilentLoopEnabled
    {
        get
        {
            lock (_sync)
            {
                return _silentLoopEnabled;
            }
        }
        set
        {
            lock (_sync)
            {
                _silentLoopEnabled = value;
                if (value)
                {
                    EnsureOutputStarted();
                }
                else if (_instances.Count == 0)
                {
                    _output?.Stop();
                }
            }
        }
    }

    public int OutputDeviceNumber
    {
        get
        {
            lock (_sync)
            {
                return _deviceNumber;
            }
        }
    }

    internal long OutputSamplesRead => Interlocked.Read(ref _outputSamplesRead);

    internal long GetSoundSamplesRead(Guid soundId)
    {
        lock (_sync)
        {
            return _instances.TryGetValue(soundId, out var instance) ? instance.SamplesRead : 0;
        }
    }

    public async Task PlayAsync(
        SoundDefinition sound,
        string resolvedPath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedPath);

        long requestVersion;
        lock (_sync)
        {
            requestVersion = NextOperationVersion(sound.Id);
        }

        SoundInstance? prepared = null;
        try
        {
            prepared = await Task.Run(
                () => SoundInstance.Create(
                    sound,
                    resolvedPath,
                    MixerFormat,
                    () => QueueNaturalCompletion(sound.Id, requestVersion)),
                cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                if (_disposed)
                {
                    prepared.Dispose();
                    prepared = null;
                    return;
                }

                if (!_soundOperationVersions.TryGetValue(sound.Id, out var currentVersion)
                    || currentVersion != requestVersion)
                {
                    prepared.Dispose();
                    prepared = null;
                    return;
                }

                EnsureOutputCreated();

                StopCore(sound.Id, PlaybackEndReason.Replaced);
                _instances.Add(sound.Id, prepared);
                _mixer.AddMixerInput(prepared.Output);
                try
                {
                    EnsureOutputStarted();
                }
                catch
                {
                    _mixer.RemoveMixerInput(prepared.Output);
                    _instances.Remove(sound.Id);
                    throw;
                }

                StateChanged?.Invoke(this, new SoundStateChangedEventArgs(sound.Id, PlaybackState.Playing));
                PlaybackStarted?.Invoke(this, new SoundPlaybackEventArgs(sound.Id));
                prepared = null;
            }

            _logger.Info($"再生開始: {sound.Name} ({sound.Id})");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            prepared?.Dispose();
            var message = $"音源「{sound.Name}」を再生できません。";
            _logger.Error(message, exception);
            PlaybackFailed?.Invoke(this, new SoundErrorEventArgs(sound.Id, message, exception));
        }
    }

    public void Stop(Guid soundId)
    {
        lock (_sync)
        {
            NextOperationVersion(soundId);
            if (_fadeOutOnStopEnabled)
            {
                StopWithFadeCore(soundId, PlaybackEndReason.Stopped);
            }
            else
            {
                StopCore(soundId, PlaybackEndReason.Stopped);
            }

            StopOutputWhenIdle();
        }
    }

    public void StopImmediately(Guid soundId)
    {
        lock (_sync)
        {
            NextOperationVersion(soundId);
            StopCore(soundId, PlaybackEndReason.Stopped);
            StopOutputWhenIdle();
        }
    }

    public void Pause(Guid soundId)
    {
        lock (_sync)
        {
            if (!_instances.TryGetValue(soundId, out var instance))
            {
                return;
            }

            instance.Pause();
            StateChanged?.Invoke(this, new SoundStateChangedEventArgs(soundId, instance.State));
        }
    }

    public void Resume(Guid soundId)
    {
        lock (_sync)
        {
            if (!_instances.TryGetValue(soundId, out var instance))
            {
                return;
            }

            instance.Resume();
            StateChanged?.Invoke(this, new SoundStateChangedEventArgs(soundId, instance.State));
        }
    }

    public void FadeVolume(Guid soundId, float targetVolume, TimeSpan duration)
    {
        lock (_sync)
        {
            if (_instances.TryGetValue(soundId, out var instance))
            {
                instance.BeginVolumeFade(targetVolume, duration);
            }
        }
    }

    public void StopAll()
    {
        lock (_sync)
        {
            foreach (var soundId in _soundOperationVersions.Keys.ToArray())
            {
                NextOperationVersion(soundId);
            }

            foreach (var soundId in _instances.Keys.ToArray())
            {
                StopCore(soundId, PlaybackEndReason.StopAll);
            }

            StopOutputWhenIdle();
        }
    }

    public PlaybackState GetState(Guid soundId)
    {
        lock (_sync)
        {
            return _instances.TryGetValue(soundId, out var instance)
                ? instance.State
                : PlaybackState.Stopped;
        }
    }

    public IReadOnlyCollection<Guid> GetActiveSoundIds()
    {
        lock (_sync)
        {
            return _instances.Keys.ToArray();
        }
    }

    public void ApplyLiveSettings(SoundDefinition sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        lock (_sync)
        {
            if (_instances.TryGetValue(sound.Id, out var instance))
            {
                instance.ApplyLiveSettings(sound);
            }
        }
    }

    public void SelectOutputDevice(int deviceNumber)
    {
        lock (_sync)
        {
            if (_deviceNumber == deviceNumber && _output is not null)
            {
                return;
            }

            foreach (var soundId in _soundOperationVersions.Keys.ToArray())
            {
                NextOperationVersion(soundId);
            }

            foreach (var soundId in _instances.Keys.ToArray())
            {
                StopCore(soundId, PlaybackEndReason.DeviceChanged);
            }

            DisposeOutput();
            _deviceNumber = deviceNumber;
            (_mixer, _masterVolumeProvider) = CreateMixer();
            EnsureOutputCreated();
            if (_silentLoopEnabled)
            {
                EnsureOutputStarted();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var soundId in _instances.Keys.ToArray())
            {
                StopCore(soundId, PlaybackEndReason.StopAll);
            }

            DisposeOutput(stopFirst: true);
        }

    }

    private (MixingSampleProvider Mixer, VolumeSampleProvider Master) CreateMixer()
    {
        var mixer = new MixingSampleProvider(MixerFormat) { ReadFully = true };
        var master = new VolumeSampleProvider(mixer);
        master.Volume = _masterMute ? 0 : _masterVolume;
        return (mixer, master);
    }

    private void EnsureOutputCreated()
    {
        if (_output is not null)
        {
            return;
        }

        try
        {
            _output = CreateOutput(_deviceNumber);
        }
        catch (Exception exception) when (_deviceNumber != -1)
        {
            FallbackToDefault(_deviceNumber, exception, startOutput: false);
        }
    }

    private void EnsureOutputStarted()
    {
        EnsureOutputCreated();
        if (_output!.PlaybackState != NAudio.Wave.PlaybackState.Playing)
        {
            try
            {
                _output.Play();
            }
            catch (Exception exception) when (_deviceNumber != -1)
            {
                FallbackToDefault(_deviceNumber, exception, startOutput: true);
            }
        }
    }

    private void ApplyMasterVolume()
    {
        _masterVolumeProvider.Volume = _masterMute ? 0 : _masterVolume;
    }

    private void StopCore(Guid soundId, PlaybackEndReason reason)
    {
        if (!_instances.Remove(soundId, out var instance))
        {
            return;
        }

        _mixer.RemoveMixerInput(instance.Output);
        instance.MarkStopped();
        instance.Dispose();
        StateChanged?.Invoke(this, new SoundStateChangedEventArgs(soundId, PlaybackState.Stopped));
        PlaybackEnded?.Invoke(this, new SoundPlaybackEventArgs(soundId, reason));
    }

    private void StopWithFadeCore(Guid soundId, PlaybackEndReason reason)
    {
        if (!_instances.TryGetValue(soundId, out var instance))
        {
            return;
        }

        if (instance.State == PlaybackState.Fading)
        {
            return;
        }

        if (_fadePolicy.GetFadeOutDuration(instance.Definition.FadeOut) is { } duration)
        {
            instance.BeginFadeOut(duration);
            StateChanged?.Invoke(this, new SoundStateChangedEventArgs(soundId, PlaybackState.Fading));
            QueueFadeCompletion(soundId, instance, duration, reason);
            return;
        }

        StopCore(soundId, reason);
    }

    private void QueueNaturalCompletion(Guid soundId, long expectedOperationVersion)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_sync)
            {
                if (!_soundOperationVersions.TryGetValue(soundId, out var currentVersion)
                    || currentVersion != expectedOperationVersion)
                {
                    return;
                }

                StopCore(soundId, PlaybackEndReason.Natural);
                StopOutputWhenIdle();
            }
        });
    }

    private void QueueFadeCompletion(
        Guid soundId,
        SoundInstance expectedInstance,
        TimeSpan duration,
        PlaybackEndReason reason)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(duration).ConfigureAwait(false);
            lock (_sync)
            {
                if (_instances.TryGetValue(soundId, out var current)
                    && ReferenceEquals(current, expectedInstance))
                {
                    StopCore(soundId, reason);
                    StopOutputWhenIdle();
                }
            }
        });
    }

    private void StopOutputWhenIdle()
    {
        if (!_silentLoopEnabled && _instances.Count == 0)
        {
            _output?.Stop();
        }
    }

    private void OnOutputPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is null)
        {
            return;
        }

        _logger.Error("音声出力デバイスでエラーが発生しました。", eventArgs.Exception);
        PlaybackFailed?.Invoke(
            this,
            new SoundErrorEventArgs(Guid.Empty, "音声出力デバイスでエラーが発生しました。", eventArgs.Exception));
        QueueOutputRecovery(eventArgs.Exception);
    }

    private IAudioOutput CreateOutput(int deviceNumber)
    {
        var activityProvider = new OutputActivitySampleProvider(
            _masterVolumeProvider,
            count => Interlocked.Add(ref _outputSamplesRead, count));
        var output = _outputFactory(deviceNumber, activityProvider);
        output.PlaybackStopped += OnOutputPlaybackStopped;
        return output;
    }

    private void FallbackToDefault(int failedDeviceNumber, Exception cause, bool startOutput)
    {
        DisposeOutput();
        _deviceNumber = -1;
        try
        {
            _output = CreateOutput(-1);
            if (startOutput)
            {
                _output.Play();
            }

            _logger.Warning($"出力デバイス{failedDeviceNumber}を使用できないため、Windows既定へ切り替えました。");
            OutputDeviceFallback?.Invoke(
                this,
                new OutputDeviceFallbackEventArgs(failedDeviceNumber, recovered: true, cause));
        }
        catch (Exception fallbackException)
        {
            DisposeOutput();
            var combined = new AggregateException(cause, fallbackException);
            _logger.Error("Windows既定の出力デバイスへの切替にも失敗しました。", combined);
            OutputDeviceFallback?.Invoke(
                this,
                new OutputDeviceFallbackEventArgs(failedDeviceNumber, recovered: false, combined));
            throw new InvalidOperationException("利用可能な音声出力デバイスがありません。", combined);
        }
    }

    private void QueueOutputRecovery(Exception cause)
    {
        if (Interlocked.Exchange(ref _outputRecoveryQueued, 1) != 0)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            OutputDeviceFallbackEventArgs? result = null;
            try
            {
                lock (_sync)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    var failedDeviceNumber = _deviceNumber;
                    foreach (var soundId in _soundOperationVersions.Keys.ToArray())
                    {
                        NextOperationVersion(soundId);
                    }

                    foreach (var soundId in _instances.Keys.ToArray())
                    {
                        StopCore(soundId, PlaybackEndReason.DeviceChanged);
                    }

                    DisposeOutput();
                    _deviceNumber = -1;
                    (_mixer, _masterVolumeProvider) = CreateMixer();
                    try
                    {
                        _output = CreateOutput(-1);
                        if (_silentLoopEnabled)
                        {
                            _output.Play();
                        }

                        _logger.Warning("出力デバイス異常後、Windows既定の出力へ自動復旧しました。");
                        result = new OutputDeviceFallbackEventArgs(failedDeviceNumber, recovered: true, cause);
                    }
                    catch (Exception fallbackException)
                    {
                        DisposeOutput();
                        var combined = new AggregateException(cause, fallbackException);
                        _logger.Error("出力デバイス異常後の自動復旧に失敗しました。", combined);
                        result = new OutputDeviceFallbackEventArgs(failedDeviceNumber, recovered: false, combined);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _outputRecoveryQueued, 0);
            }

            if (result is not null)
            {
                OutputDeviceFallback?.Invoke(this, result);
            }
        });
    }

    private void DisposeOutput(bool stopFirst = false)
    {
        if (_output is null)
        {
            return;
        }

        var output = _output;
        _output = null;
        output.PlaybackStopped -= OnOutputPlaybackStopped;
        try
        {
            if (stopFirst)
            {
                output.Stop();
            }
        }
        finally
        {
            output.Dispose();
        }
    }

    private long NextOperationVersion(Guid soundId)
    {
        var next = _soundOperationVersions.TryGetValue(soundId, out var current)
            ? unchecked(current + 1)
            : 1;
        _soundOperationVersions[soundId] = next;
        return next;
    }
}
