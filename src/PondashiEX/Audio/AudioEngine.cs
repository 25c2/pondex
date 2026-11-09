using System.Diagnostics;
using System.Globalization;
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
    private readonly Func<SoundDefinition, string, WaveFormat, Action, SoundInstance> _prepareSound;
    private readonly PlaybackStartDiagnostics _startDiagnostics = new();
    private MixingSampleProvider _mixer;
    private VolumeSampleProvider _masterVolumeProvider;
    private IAudioOutput? _output;
    private int _deviceNumber = -1;
    private float _masterVolume = 1.0f;
    private bool _masterMute;
    private bool _fadeOutOnStopEnabled = true;
    private bool _silentLoopEnabled;
    private bool _disposed;
    private bool _outputFaulted;
    private int _outputRecoveryQueued;
    private long _outputSamplesRead;
    private OutputActivitySampleProvider? _activityProvider;
    private long _outputPositionBase;

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
        IFadePolicy? fadePolicy = null,
        Func<SoundDefinition, string, WaveFormat, Action, SoundInstance>? prepareSound = null)
    {
        _logger = logger;
        _outputFactory = outputFactory;
        _fadePolicy = fadePolicy ?? new SecondsFadePolicy();
        _prepareSound = prepareSound ?? SoundInstance.Create;
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

    public bool PlaybackTimingEnabled
    {
        get => _startDiagnostics.Enabled;
        set => _startDiagnostics.Enabled = value;
    }

    public IReadOnlyList<PlaybackStartTiming> GetPlaybackStartTimings() => _startDiagnostics.Snapshot();

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
        var timingRequest = _startDiagnostics.Begin(sound.Id);
        var diagnosticStarted = _logger.DetailedLoggingEnabled ? Stopwatch.GetTimestamp() : 0;
        PlaybackStartTiming? timing = null;

        long requestVersion;
        lock (_sync)
        {
            requestVersion = NextOperationVersion(sound.Id);
        }
        if (_logger.DetailedLoggingEnabled)
        {
            _logger.Debug($"play requested: sound={sound.Id} request={requestVersion} path={resolvedPath}");
            LogSoundSettings(sound);
        }

        SoundInstance? prepared = null;
        SoundInstance? completionInstance = null;
        try
        {
            prepared = await Task.Run(
                () => _prepareSound(
                    sound,
                    resolvedPath,
                    MixerFormat,
                    () => QueueNaturalCompletion(sound.Id, completionInstance)),
                cancellationToken).ConfigureAwait(false);
            completionInstance = prepared;
            var preparedAt = timingRequest.Attempt > 0 ? Stopwatch.GetTimestamp() : 0;
            if (diagnosticStarted != 0 && _logger.DetailedLoggingEnabled)
            {
                _logger.Debug(string.Create(CultureInfo.InvariantCulture,
                    $"play prepared: sound={sound.Id} request={requestVersion} prepare_ms={Stopwatch.GetElapsedTime(diagnosticStarted).TotalMilliseconds:F3}"));
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    if (_logger.DetailedLoggingEnabled) { _logger.Debug($"play discarded: sound={sound.Id} request={requestVersion} reason=disposed"); }
                    prepared.Dispose();
                    prepared = null;
                    return;
                }

                if (!_soundOperationVersions.TryGetValue(sound.Id, out var currentVersion)
                    || currentVersion != requestVersion)
                {
                    if (_logger.DetailedLoggingEnabled) { _logger.Debug($"play discarded: sound={sound.Id} request={requestVersion} current_request={currentVersion} reason=superseded"); }
                    prepared.Dispose();
                    prepared = null;
                    return;
                }

                // Settings may have changed while the decoder was being prepared.
                // In particular, never start with a stale volume-boost permission.
                prepared.ApplyLiveSettings(sound);
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

                if (timingRequest.Attempt > 0)
                {
                    timing = _startDiagnostics.Complete(sound.Id, timingRequest, preparedAt, Stopwatch.GetTimestamp());
                }
                StateChanged?.Invoke(this, new SoundStateChangedEventArgs(sound.Id, PlaybackState.Playing));
                PlaybackStarted?.Invoke(this, new SoundPlaybackEventArgs(sound.Id));
                prepared = null;
                if (_logger.DetailedLoggingEnabled)
                {
                    _logger.Debug($"play started: sound={sound.Id} request={requestVersion} device={_deviceNumber} active={_instances.Count}");
                }
            }

            _logger.Info($"再生開始: {sound.Name} ({sound.Id})");
            if (timing is not null)
            {
                // Outside both the audio callback and engine lock; FileLogger enqueues asynchronously.
                _logger.Info(string.Create(CultureInfo.InvariantCulture,
                    $"再生開始計測: {sound.Id} attempt={timing.Attempt} prepare_ms={timing.PreparationMilliseconds:F3} start_ms={timing.StartMilliseconds:F3}（内部処理時間。実際の出音遅延ではありません）"));
            }
        }
        catch (OperationCanceledException)
        {
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"play cancelled: sound={sound.Id} request={requestVersion}"); }
            throw;
        }
        catch (Exception exception)
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
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"stop requested: sound={soundId} fade={_fadeOutOnStopEnabled}"); }
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
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"stop requested: sound={soundId} fade=False immediate=True"); }
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
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"pause: sound={soundId} state={instance.State}"); }
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
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"resume: sound={soundId} state={instance.State}"); }
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
                if (_logger.DetailedLoggingEnabled) { _logger.Debug($"volume fade: sound={soundId} target={targetVolume} duration_ms={duration.TotalMilliseconds}"); }
            }
        }
    }

    public void StopAll(bool useFadeOut = false)
    {
        lock (_sync)
        {
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"stop_all requested: fade={useFadeOut} active={_instances.Count}"); }
            foreach (var soundId in _soundOperationVersions.Keys.ToArray())
            {
                NextOperationVersion(soundId);
            }

            foreach (var soundId in _instances.Keys.ToArray())
            {
                if (useFadeOut)
                {
                    StopWithFadeCore(soundId, PlaybackEndReason.StopAll);
                }
                else
                {
                    StopCore(soundId, PlaybackEndReason.StopAll);
                }
            }

            StopOutputWhenIdle();
        }
    }

    public void EmergencyStopAll()
    {
        lock (_sync)
        {
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"emergency_stop requested: device={_deviceNumber} active={_instances.Count}"); }
            // StopAll also invalidates every in-flight preparation before disposing active instances.
            StopAll(useFadeOut: false);
            // Release the device handle as well as flushing its queued buffers.
            // A later explicit play/selection must open a fresh handle.
            DisposeOutput(stopFirst: true);
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
        if (_logger.DetailedLoggingEnabled) { LogSoundSettings(sound); }
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
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"output selection: previous={_deviceNumber} requested={deviceNumber} faulted={_outputFaulted}"); }
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
            _outputFaulted = false;
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

        if (_outputFaulted)
        {
            throw new InvalidOperationException("出力デバイス異常のため停止中です。設定で出力先を確認して選び直してください。");
        }

        try
        {
            _output = CreateOutput(_deviceNumber);
        }
        catch (Exception exception)
        {
            _outputFaulted = true;
            OutputDeviceFallback?.Invoke(this, new OutputDeviceFallbackEventArgs(_deviceNumber, false, exception));
            throw;
        }
    }

    private void EnsureOutputStarted()
    {
        EnsureOutputCreated();
        if (_output!.PlaybackState != NAudio.Wave.PlaybackState.Playing)
        {
            try
            {
                _outputPositionBase = _activityProvider?.CompletionPosition ?? 0;
                _output.Play();
            }
            catch (Exception exception)
            {
                QueueOutputRecovery(exception);
                throw;
            }
        }
    }

    private void ApplyMasterVolume()
    {
        _masterVolumeProvider.Volume = _masterMute ? 0 : _masterVolume;
        if (_logger.DetailedLoggingEnabled) { _logger.Debug($"master changed: volume={_masterVolume} mute={_masterMute}"); }
    }

    private void LogSoundSettings(SoundDefinition sound) => _logger.Debug(string.Create(CultureInfo.InvariantCulture,
        $"sound settings: sound={sound.Id} volume={sound.Volume} boost={sound.AllowVolumeBoost} pan={sound.Pan} pitch={sound.Pitch} speed={sound.Speed} "
        + $"loop={sound.Loop.Enabled} loop_sample={sound.Loop.StartSample} loop_seconds={sound.Loop.StartSeconds} "
        + $"fade_in={sound.FadeIn.Enabled}/{sound.FadeIn.Value} fade_out={sound.FadeOut.Enabled}/{sound.FadeOut.Value}"));

    private void StopCore(Guid soundId, PlaybackEndReason reason)
    {
        if (!_instances.Remove(soundId, out var instance))
        {
            return;
        }

        _mixer.RemoveMixerInput(instance.Output);
        if (_logger.DetailedLoggingEnabled)
        {
            _logger.Debug($"play ended: sound={soundId} reason={reason} previous_state={instance.State} samples_read={instance.SamplesRead} active={_instances.Count}");
        }
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

        if (instance.State == PlaybackState.Paused)
        {
            StopCore(soundId, reason);
            return;
        }

        if (_fadePolicy.GetFadeOutDuration(instance.Definition.FadeOut) is { } duration)
        {
            instance.CompletionReason = reason;
            instance.BeginFadeOut(duration);
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"stop fade started: sound={soundId} reason={reason} duration_ms={duration.TotalMilliseconds}"); }
            StateChanged?.Invoke(this, new SoundStateChangedEventArgs(soundId, PlaybackState.Fading));
            return;
        }

        StopCore(soundId, reason);
    }

    private void QueueNaturalCompletion(Guid soundId, SoundInstance? expectedInstance)
    {
        // Rendering happens ahead of the device. Capture the end of this output
        // block while still in Read, then wait off the audio callback for playback.
        var expectedOutput = _output;
        var target = (_activityProvider?.CompletionPosition ?? 0) - _outputPositionBase;
        _ = Task.Run(async () =>
        {
            var waitStarted = Stopwatch.GetTimestamp();
            while (true)
            {
                lock (_sync)
                {
                    if (_disposed || !ReferenceEquals(_output, expectedOutput)
                        || !_instances.TryGetValue(soundId, out var current)
                        || !ReferenceEquals(current, expectedInstance))
                    {
                        return;
                    }

                    if (Stopwatch.GetElapsedTime(waitStarted) > TimeSpan.FromSeconds(5))
                    {
                        QueueOutputRecovery(new TimeoutException("音声出力位置が進まないため安全停止しました。"));
                        return;
                    }

                    try
                    {
                        if (expectedOutput is null || expectedOutput.HasConsumedSamples(target))
                        {
                            StopCore(soundId, current.CompletionReason);
                            StopOutputWhenIdle();
                            return;
                        }
                    }
                    catch (Exception exception)
                    {
                        // Let the existing output recovery path report device failure.
                        QueueOutputRecovery(exception);
                        return;
                    }
                }
                await Task.Delay(10).ConfigureAwait(false);
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
            // An intentional Stop may notify while its caller holds _sync.
            // Diagnose off the callback, checking the handle and current state.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (_sync)
                {
                    if (!_disposed && ReferenceEquals(sender, _output) && _instances.Count > 0
                        && _output is { PlaybackState: NAudio.Wave.PlaybackState.Stopped })
                    {
                        QueueOutputRecovery(new InvalidOperationException("再生中に音声出力が停止しました。"));
                    }
                }
            });
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
        _activityProvider = activityProvider;
        _outputPositionBase = 0;
        output.PlaybackStopped += OnOutputPlaybackStopped;
        return output;
    }

    private void QueueOutputRecovery(Exception cause)
    {
        if (Interlocked.Exchange(ref _outputRecoveryQueued, 1) != 0)
        {
            return;
        }

        var expectedOutput = _output;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            OutputDeviceFallbackEventArgs? result = null;
            try
            {
                lock (_sync)
                {
                    if (_disposed || !ReferenceEquals(_output, expectedOutput))
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
                    _outputFaulted = true;
                    (_mixer, _masterVolumeProvider) = CreateMixer();
                    _logger.Error("出力デバイス異常のため停止しました。設定で出力先を選び直してください。", cause);
                    result = new OutputDeviceFallbackEventArgs(failedDeviceNumber, recovered: false, cause);
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
