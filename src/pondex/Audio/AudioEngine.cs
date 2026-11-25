using System.Diagnostics;
using System.Globalization;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using pondex.Project;
using pondex.Utility;

namespace pondex.Audio;

public sealed class AudioEngine : IAudioEngine
{
    private static readonly WaveFormat MixerFormat = WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2);
    private readonly object _sync = new();
    private readonly Dictionary<Guid, SoundInstance> _instances = [];
    private readonly Dictionary<Guid, long> _soundOperationVersions = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _preparationCancellations = [];
    private readonly Dictionary<Guid, SemaphoreSlim> _preparationLanes = [];
    private const int PreparationCapacity = 4;
    private readonly SemaphoreSlim _preparationSlots = new(PreparationCapacity, PreparationCapacity);
    private readonly Dictionary<(Guid SoundId, long Request), (DateTimeOffset Started, long Timestamp)> _activePreparations = [];
    private long _preparationRequests, _preparationCompleted, _preparationDiscarded, _preparationFailed;
    private long _preparationTicks;
    private int _preparationsActive, _preparationsPeak;
    private readonly IAppLogger _logger;
    private readonly Func<int, ISampleProvider, IAudioOutput> _outputFactory;
    private readonly IFadePolicy _fadePolicy;
    private readonly Func<SoundDefinition, string, WaveFormat, Action, SoundInstance>? _prepareSound;
    private int _emergencyStopRequested;
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
        _prepareSound = prepareSound;
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

    internal bool NeedsOutputOpen
    {
        get { lock (_sync) { return _output is null; } }
    }

    internal bool IsOutputFaulted
    {
        get { lock (_sync) { return _outputFaulted; } }
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

    internal AudioPreparationStatistics PreparationStatistics => new(
        Interlocked.Read(ref _preparationRequests), Interlocked.Read(ref _preparationCompleted),
        Interlocked.Read(ref _preparationDiscarded), Interlocked.Read(ref _preparationFailed),
        Volatile.Read(ref _preparationsActive), Volatile.Read(ref _preparationsPeak),
        TimeSpan.FromTicks(Interlocked.Read(ref _preparationTicks)));

    internal IReadOnlyList<ActiveAudioPreparation> GetActivePreparations()
    {
        lock (_sync)
        {
            return _activePreparations.Select(entry => new ActiveAudioPreparation(entry.Key.SoundId, entry.Key.Request,
                entry.Value.Started, Stopwatch.GetElapsedTime(entry.Value.Timestamp))).ToArray();
        }
    }

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

    public Task PlayAsync(SoundDefinition sound, string resolvedPath, CancellationToken cancellationToken = default) =>
        StartPlaybackAsync(sound, resolvedPath, cancellationToken: cancellationToken);

    internal Task<PlaybackStartResult> StartPlaybackAsync(
        SoundDefinition sound, string resolvedPath, Func<CancellationToken, Task>? verifyOutput = null,
        CancellationToken cancellationToken = default) =>
        StartPlaybackMeasuredAsync(sound, resolvedPath, verifyOutput, null, cancellationToken);

    internal Task<PlaybackStartResult> StartPlaybackMeasuredAsync(
        SoundDefinition sound, string resolvedPath, Func<CancellationToken, Task>? verifyOutput,
        OperationTiming? trace, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedPath);
        return StartPlaybackCoreAsync(sound, resolvedPath, null, verifyOutput, trace, cancellationToken);
    }

    internal Task<PlaybackStartResult> StartProjectPlaybackAsync(
        SoundDefinition sound, string storedPath, Func<string> resolvePath,
        Func<CancellationToken, Task>? verifyOutput, OperationTiming? trace) =>
        StartPlaybackCoreAsync(sound, storedPath, resolvePath, verifyOutput, trace, CancellationToken.None);

    private async Task<PlaybackStartResult> StartPlaybackCoreAsync(
        SoundDefinition sound, string path, Func<string>? resolvePath, Func<CancellationToken, Task>? verifyOutput,
        OperationTiming? trace, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(sound);
        var timingRequest = _startDiagnostics.Begin(sound.Id);
        var reportHere = trace is null;
        trace ??= _logger.DetailedLoggingEnabled || timingRequest.Attempt > 0 ? new OperationTiming() : null;
        var reportAsInfo = timingRequest.Attempt > 0 && !_logger.DetailedLoggingEnabled;
        void SetOutcome(string outcome)
        {
            if (trace is not null) { trace.Outcome = outcome; }
        }
        var diagnosticStarted = Stopwatch.GetTimestamp();
        PlaybackStartTiming? timing = null;

        long requestVersion;
        CancellationTokenSource preparationCancellation;
        SemaphoreSlim lane;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Volatile.Read(ref _emergencyStopRequested) != 0)
            {
                SetOutcome("emergency_stop");
                return PlaybackStartResult.Cancelled;
            }
            requestVersion = NextOperationVersion(sound.Id);
            preparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _preparationCancellations[sound.Id] = preparationCancellation;
            if (!_preparationLanes.TryGetValue(sound.Id, out lane!))
            {
                lane = new SemaphoreSlim(1, 1);
                _preparationLanes.Add(sound.Id, lane);
            }
        }
        Interlocked.Increment(ref _preparationRequests);
        if (_logger.DetailedLoggingEnabled)
        {
            _logger.Debug($"play requested: sound={sound.Id} request={requestVersion} trace={trace?.Id:N} path={path}");
            LogSoundSettings(sound);
        }

        SoundInstance? prepared = null;
        SoundInstance? completionInstance = null;
        var laneAcquired = false;
        var slotAcquired = false;
        var outputStartFailed = false;
        try
        {
            var token = preparationCancellation.Token;
            // One decoder per sound, up to four different sounds concurrently.
            // Cancellation removes obsolete waiters before they open any file.
            using (trace?.Measure("lane_wait")) { await lane.WaitAsync(token).ConfigureAwait(false); }
            laneAcquired = true;
            using (trace?.Measure("slot_wait")) { await _preparationSlots.WaitAsync(token).ConfigureAwait(false); }
            slotAcquired = true;
            var workerQueued = trace is not null ? Stopwatch.GetTimestamp() : 0;
            // Decoder opens can block in a driver/file system. Keep those bounded
            // workers off the shared pool used by completion and logging tasks.
            prepared = await Task.Factory.StartNew(
                () =>
                {
                    trace?.Record("worker_wait", workerQueued);
                    token.ThrowIfCancellationRequested();
                    var started = Stopwatch.GetTimestamp();
                    var startedAt = DateTimeOffset.Now;
                    lock (_sync) { _activePreparations.Add((sound.Id, requestVersion), (startedAt, started)); }
                    var active = Interlocked.Increment(ref _preparationsActive);
                    if (_logger.DetailedLoggingEnabled)
                    {
                        _logger.Debug(string.Create(CultureInfo.InvariantCulture,
                            $"decoder start: sound={sound.Id} request={requestVersion} started={startedAt:O} active={active}/{PreparationCapacity} queued_ms={Stopwatch.GetElapsedTime(diagnosticStarted, started).TotalMilliseconds:F3}"));
                    }
                    int peak;
                    do { peak = Volatile.Read(ref _preparationsPeak); }
                    while (active > peak && Interlocked.CompareExchange(ref _preparationsPeak, active, peak) != peak);
                    try
                    {
                        using var measurement = trace?.Measure("prepare");
                        var resolvedPath = path;
                        if (resolvePath is not null)
                        {
                            using var pathTiming = trace?.Measure("resolve_path");
                            resolvedPath = resolvePath();
                            token.ThrowIfCancellationRequested();
                        }

                        void Completed() => QueueNaturalCompletion(sound.Id, completionInstance);
                        var result = _prepareSound is null
                            ? SoundInstance.CreateMeasured(sound, resolvedPath, MixerFormat, Completed, trace, resolvePath is not null)
                            : _prepareSound(sound, resolvedPath, MixerFormat, Completed);
                        Interlocked.Increment(ref _preparationCompleted);
                        return result;
                    }
                    finally
                    {
                        lock (_sync) { _activePreparations.Remove((sound.Id, requestVersion)); }
                        Interlocked.Decrement(ref _preparationsActive);
                        var elapsed = Stopwatch.GetElapsedTime(started);
                        Interlocked.Add(ref _preparationTicks, elapsed.Ticks);
                        if (elapsed >= TimeSpan.FromSeconds(5))
                        {
                            _logger.Warning(string.Create(CultureInfo.InvariantCulture,
                                $"decoder finished after delay: sound={sound.Id} request={requestVersion} elapsed_ms={elapsed.TotalMilliseconds:F3}"));
                        }
                    }
                }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);
            completionInstance = prepared;
            prepared.SetReadFailureHandler(exception => QueueSoundReadFailure(sound.Id, completionInstance, exception));
            var ready = trace is not null ? Stopwatch.GetTimestamp() : 0;
            if (verifyOutput is not null)
            {
                using var verify = trace?.Measure("recheck_output");
                await verifyOutput(token).ConfigureAwait(false);
            }
            // Stop/device changes are accounted for as superseded requests below;
            // explicit caller cancellation must also dispose a decoder already in flight.
            cancellationToken.ThrowIfCancellationRequested();
            var preparedAt = timingRequest.Attempt > 0 ? Stopwatch.GetTimestamp() : 0;
            if (_logger.DetailedLoggingEnabled)
            {
                _logger.Debug(string.Create(CultureInfo.InvariantCulture,
                    $"play prepared: sound={sound.Id} request={requestVersion} prepare_ms={Stopwatch.GetElapsedTime(diagnosticStarted).TotalMilliseconds:F3}"));
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    Interlocked.Increment(ref _preparationDiscarded);
                    if (_logger.DetailedLoggingEnabled) { _logger.Debug($"play discarded: sound={sound.Id} request={requestVersion} reason=disposed"); }
                    SetOutcome(_disposed ? "engine_disposed" : "superseded");
                    using (trace?.Measure("discard_dispose")) { prepared.Dispose(); }
                    prepared = null;
                    return PlaybackStartResult.Cancelled;
                }

                var currentVersion = _soundOperationVersions.GetValueOrDefault(sound.Id);
                if (Volatile.Read(ref _emergencyStopRequested) != 0 || currentVersion != requestVersion)
                {
                    Interlocked.Increment(ref _preparationDiscarded);
                    if (_logger.DetailedLoggingEnabled) { _logger.Debug($"play discarded: sound={sound.Id} request={requestVersion} current_request={currentVersion} reason=superseded"); }
                    SetOutcome(_disposed ? "engine_disposed" : "superseded");
                    using (trace?.Measure("discard_dispose")) { prepared.Dispose(); }
                    prepared = null;
                    return PlaybackStartResult.Cancelled;
                }

                // Settings may have changed while the decoder was being prepared.
                // In particular, never start with a stale volume-boost permission.
                using (trace?.Measure("apply_live_settings")) { prepared.ApplyLiveSettings(sound); }
                using (trace?.Measure("output_open")) { EnsureOutputCreated(); }
                using (trace?.Measure("mixer_register"))
                {
                    StopCore(sound.Id, PlaybackEndReason.Replaced);
                    _instances.Add(sound.Id, prepared);
                    _mixer.AddMixerInput(prepared.Output);
                }
                try
                {
                    using (trace?.Measure("output_start")) { EnsureOutputStarted(); }
                    trace?.Record("ready_to_start", ready);
                }
                catch
                {
                    outputStartFailed = true;
                    _mixer.RemoveMixerInput(prepared.Output);
                    _instances.Remove(sound.Id);
                    throw;
                }

                if (timingRequest.Attempt > 0)
                {
                    timing = _startDiagnostics.Complete(sound.Id, timingRequest, preparedAt, Stopwatch.GetTimestamp());
                }
                prepared = null;
                Notify(StateChanged, new SoundStateChangedEventArgs(sound.Id, PlaybackState.Playing));
                Notify(PlaybackStarted, new SoundPlaybackEventArgs(sound.Id));
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
            SetOutcome("started");
            return PlaybackStartResult.Started;
        }
        catch (OperationCanceledException)
        {
            SetOutcome(cancellationToken.IsCancellationRequested ? "cancelled" : "superseded");
            using (trace?.Measure("discard_dispose")) { prepared?.Dispose(); }
            Interlocked.Increment(ref _preparationDiscarded);
            if (_logger.DetailedLoggingEnabled) { _logger.Debug($"play cancelled: sound={sound.Id} request={requestVersion}"); }
            if (cancellationToken.IsCancellationRequested) { throw; }
            return PlaybackStartResult.Cancelled;
        }
        catch (Exception exception)
        {
            var missing = exception is FileNotFoundException or DirectoryNotFoundException;
            SetOutcome($"{(missing ? "missing" : "failed")}_{exception.GetType().Name}");
            using (trace?.Measure("discard_dispose")) { prepared?.Dispose(); }
            Interlocked.Increment(ref _preparationFailed);
            lock (_sync)
            {
                // Output recovery can invalidate request versions after Play throws.
                // Still report an actual start failure from the committed request.
                if (_disposed || (!outputStartFailed && _soundOperationVersions.GetValueOrDefault(sound.Id) != requestVersion))
                {
                    Interlocked.Increment(ref _preparationDiscarded);
                    if (_logger.DetailedLoggingEnabled) { _logger.Debug($"play discarded: sound={sound.Id} request={requestVersion} reason=obsolete_prepare_failure"); }
                    SetOutcome("superseded");
                    return PlaybackStartResult.Cancelled;
                }
            }
            var message = $"音源「{sound.Name}」を再生できません。";
            _logger.Error(message, exception);
            PlaybackFailed?.Invoke(this, new SoundErrorEventArgs(sound.Id, message, exception));
            return missing ? PlaybackStartResult.Missing : PlaybackStartResult.Failed;
        }
        finally
        {
            using (trace?.Measure("request_cleanup"))
            {
                if (slotAcquired) { _preparationSlots.Release(); }
                if (laneAcquired) { lane.Release(); }
                lock (_sync)
                {
                    if (_preparationCancellations.GetValueOrDefault(sound.Id) == preparationCancellation)
                    {
                        _preparationCancellations.Remove(sound.Id);
                    }
                    if (!_preparationCancellations.ContainsKey(sound.Id) && lane.CurrentCount == 1)
                    {
                        _preparationLanes.Remove(sound.Id);
                    }
                }
                preparationCancellation.Dispose();
            }
            if (trace is not null && reportHere)
            {
                var line = $"play_timing sound={sound.Id} origin=engine {trace.Format()}";
                if (reportAsInfo) { _logger.Info(line); }
                else { _logger.Debug(line); }
            }
        }
    }

    internal async Task RunPreflightDecodeAsync(Action decode, CancellationToken cancellationToken)
    {
        await _preparationSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(decode, cancellationToken).ConfigureAwait(false);
        }
        finally { _preparationSlots.Release(); }
    }

    internal void CancelPendingPlayback(Guid soundId)
    {
        lock (_sync) { NextOperationVersion(soundId); }
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

    internal Task RequestEmergencyStopAsync()
    {
        Interlocked.Exchange(ref _emergencyStopRequested, 1);
        return Task.Run(() =>
        {
            try { EmergencyStopAll(); }
            finally { Interlocked.Exchange(ref _emergencyStopRequested, 0); }
        });
    }

    public void EmergencyStopAll()
    {
        lock (_sync)
        {
            if (_logger.DetailedLoggingEnabled)
            {
                _logger.Debug($"emergency_stop requested: device={_deviceNumber} active={_instances.Count} decoding={_activePreparations.Count}/{PreparationCapacity}");
                foreach (var job in GetActivePreparations())
                {
                    _logger.Debug(string.Create(CultureInfo.InvariantCulture,
                        $"decoder still occupies slot: sound={job.SoundId} request={job.Request} started={job.Started:O} elapsed_ms={job.Elapsed.TotalMilliseconds:F3}"));
                }
            }
            foreach (var soundId in _soundOperationVersions.Keys.ToArray()) { NextOperationVersion(soundId); }
            Exception? failure = null;
            try { DisposeOutput(stopFirst: true); }
            catch (Exception exception) { failure = exception; }
            foreach (var soundId in _instances.Keys.ToArray())
            {
                try { StopCore(soundId, PlaybackEndReason.StopAll); }
                catch (Exception exception) { failure ??= exception; }
            }

            if (failure is not null)
            {
                _outputFaulted = true;
                _logger.Error("緊急停止中に音声出力を終了できませんでした。", failure);
            }
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
            foreach (var cancellation in _preparationCancellations.Values) { cancellation.Cancel(); }
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
        try { instance.Dispose(); }
        catch (Exception exception) { _logger.Error($"音源を閉じられませんでした: {soundId}", exception); }
        Notify(StateChanged, new SoundStateChangedEventArgs(soundId, PlaybackState.Stopped));
        Notify(PlaybackEnded, new SoundPlaybackEventArgs(soundId, reason));
    }

    private void QueueSoundReadFailure(Guid soundId, SoundInstance? expected, Exception exception)
    {
        _ = Task.Run(() =>
        {
            lock (_sync)
            {
                if (_disposed || !_instances.TryGetValue(soundId, out var current) || current != expected) { return; }
                StopCore(soundId, PlaybackEndReason.Error);
                var message = $"音源「{current.Definition.Name}」の読み出しを停止しました。";
                _logger.Error(message, exception);
                Notify(PlaybackFailed, new SoundErrorEventArgs(soundId, message, exception));
                try { StopOutputWhenIdle(); }
                catch (Exception outputError) { QueueOutputRecovery(outputError); }
            }
        });
    }

    private void Notify<T>(EventHandler<T>? handlers, T args)
    {
        if (handlers is null) { return; }
        foreach (var handler in Delegate.EnumerateInvocationList(handlers))
        {
            try { handler(this, args); }
            catch (Exception exception) { _logger.Error("再生通知の処理に失敗しました。", exception); }
        }
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

                    _outputFaulted = true;
                    try { DisposeOutput(); }
                    catch (Exception exception) { _logger.Error("異常が発生した出力を閉じられませんでした。", exception); }
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
        Exception? stopError = null;
        try
        {
            if (stopFirst) { output.Stop(); }
        }
        catch (Exception exception) { stopError = exception; }
        try { output.Dispose(); }
        catch (Exception exception) when (stopError is not null)
        {
            _logger.Error("停止失敗後に音声出力を解放できませんでした。", exception);
        }
        if (stopError is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(stopError); }
    }

    private long NextOperationVersion(Guid soundId)
    {
        if (_preparationCancellations.TryGetValue(soundId, out var cancellation)) { cancellation.Cancel(); }
        var next = _soundOperationVersions.TryGetValue(soundId, out var current)
            ? unchecked(current + 1)
            : 1;
        _soundOperationVersions[soundId] = next;
        return next;
    }
}

internal readonly record struct AudioPreparationStatistics(long Requested, long Completed, long Discarded,
    long Failed, int Active, int Peak, TimeSpan PreparationTime);

internal sealed record ActiveAudioPreparation(Guid SoundId, long Request, DateTimeOffset Started, TimeSpan Elapsed);
