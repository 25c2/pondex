using PondashiEX.Project;

namespace PondashiEX.Trigger;

public sealed class TriggerEngine
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, SoundClock> _soundStarted = [];
    private readonly HashSet<Guid> _soundEndedSinceTick = [];
    private readonly HashSet<Guid> _latchedTriggers = [];
    private readonly TimeProvider _timeProvider;
    private IReadOnlyList<TriggerDefinition> _triggers = [];
    private DateTimeOffset? _lastTickNow;
    private TimeSpan _lastStopwatchElapsed;
    private bool _stopwatchHasStarted;
    private bool _resumeNeedsBaseline;
    private long _dispatchGeneration;
    private bool _requireFreshSoundStart;

    private bool _suspended;

    public bool IsSuspended { get { lock (_sync) { return _suspended; } } }

    public void Suspend()
    {
        lock (_sync)
        {
            _suspended = true;
            _requireFreshSoundStart = true;
            ClearSoundNotifications();
            _dispatchGeneration++;
        }
    }

    public void Resume(DateTimeOffset? now = null, TimeSpan? stopwatchElapsed = null, bool stopwatchRunning = false)
    {
        lock (_sync)
        {
            if (!_suspended) { return; }
            ClearSoundNotifications();
            _suspended = false;
            _resumeNeedsBaseline = true;
            if (now is { } baselineNow && stopwatchElapsed is { } elapsed)
            {
                EstablishResumeBaseline(baselineNow, elapsed, stopwatchRunning);
            }
        }
    }

    private void ClearSoundNotifications()
    {
        _soundStarted.Clear();
        _soundEndedSinceTick.Clear();
    }

    private void EstablishResumeBaseline(DateTimeOffset now, TimeSpan elapsed, bool running)
    {
        if (elapsed < _lastStopwatchElapsed) { ResetStopwatch(); }
        _stopwatchHasStarted |= running || elapsed > TimeSpan.Zero;
        if (_stopwatchHasStarted)
        {
            foreach (var trigger in _triggers.Where(trigger => trigger.Enabled
                && trigger.TriggerType == TriggerType.StopwatchElapsed
                && trigger.Parameters.Elapsed is { } target && target <= elapsed))
            {
                _latchedTriggers.Add(trigger.Id);
            }
        }
        _lastTickNow = now;
        _lastStopwatchElapsed = elapsed;
        _resumeNeedsBaseline = false;
    }

    public void ResetStopwatch()
    {
        lock (_sync)
        {
            foreach (var trigger in _triggers.Where(trigger => trigger.TriggerType == TriggerType.StopwatchElapsed))
            {
                _latchedTriggers.Remove(trigger.Id);
            }
            _lastStopwatchElapsed = TimeSpan.Zero;
            _stopwatchHasStarted = false;
            _dispatchGeneration++;
        }
    }

    public TriggerEngine(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<TriggerFiredEventArgs>? TriggerFired;

    public void SetTriggers(IReadOnlyList<TriggerDefinition> triggers, bool preserveRuntimeState = false)
    {
        lock (_sync)
        {
            _triggers = triggers;
            _dispatchGeneration++;
            if (!preserveRuntimeState)
            {
                _soundStarted.Clear();
                _soundEndedSinceTick.Clear();
                _latchedTriggers.Clear();
                _lastTickNow = null;
                _lastStopwatchElapsed = TimeSpan.Zero;
                _stopwatchHasStarted = false;
                _resumeNeedsBaseline = false;
                _requireFreshSoundStart = false;
                return;
            }

            var triggerIds = triggers
                .Where(trigger => trigger.Enabled)
                .Select(trigger => trigger.Id)
                .ToHashSet();
            _latchedTriggers.RemoveWhere(triggerId => !triggerIds.Contains(triggerId));
        }
    }

    public void OnSoundStarted(Guid soundId)
    {
        lock (_sync)
        {
            if (_suspended) { return; }
            _soundStarted[soundId] = new SoundClock(_timeProvider.GetTimestamp());
            _soundEndedSinceTick.Remove(soundId);
            foreach (var trigger in _triggers.Where(item =>
                         item.TriggerType == TriggerType.SoundElapsed
                         && item.Parameters.SourceSoundId == soundId))
            {
                _latchedTriggers.Remove(trigger.Id);
            }
        }
    }

    public void OnSoundEnded(Guid soundId, bool naturalEnd)
    {
        lock (_sync)
        {
            if (_suspended) { return; }
            var wasStarted = _soundStarted.Remove(soundId);
            if (_requireFreshSoundStart && !wasStarted) { return; }
            if (naturalEnd)
            {
                _soundEndedSinceTick.Add(soundId);
            }
        }
    }

    public void SetSoundPaused(Guid soundId, bool paused)
    {
        lock (_sync)
        {
            if (!_soundStarted.TryGetValue(soundId, out var clock) || clock.Paused == paused)
            {
                return;
            }

            if (paused)
            {
                clock.Elapsed += _timeProvider.GetElapsedTime(clock.Started);
            }
            else
            {
                clock.Started = _timeProvider.GetTimestamp();
            }

            clock.Paused = paused;
        }
    }

    public IReadOnlyList<TriggerDefinition> Tick(
        DateTimeOffset now,
        TimeSpan stopwatchElapsed,
        bool stopwatchRunning = true)
    {
        List<TriggerDefinition> fired;
        long generation;
        lock (_sync)
        {
            if (_suspended) { return []; }
            if (_resumeNeedsBaseline) { EstablishResumeBaseline(now, stopwatchElapsed, stopwatchRunning); }
            if (stopwatchElapsed < _lastStopwatchElapsed) { ResetStopwatch(); }
            _stopwatchHasStarted |= stopwatchRunning;
            _lastStopwatchElapsed = stopwatchElapsed;
            generation = _dispatchGeneration;
            fired = [];
            foreach (var trigger in _triggers.Where(trigger => trigger.Enabled))
            {
                var shouldFire = (trigger.TriggerType != TriggerType.StopwatchElapsed || stopwatchRunning)
                    && ShouldFire(trigger, now, stopwatchElapsed, _lastTickNow);
                if (!shouldFire)
                {
                    if (trigger.TriggerType == TriggerType.ClockTime
                        || (trigger.TriggerType == TriggerType.StopwatchElapsed && stopwatchRunning))
                    {
                        _latchedTriggers.Remove(trigger.Id);
                    }

                    continue;
                }

                if (trigger.TriggerType is TriggerType.ClockTime or TriggerType.SoundEnded
                    || _latchedTriggers.Add(trigger.Id))
                {
                    fired.Add(trigger);
                }
            }

            _soundEndedSinceTick.Clear();
            _lastTickNow = now;
        }

        foreach (var trigger in fired)
        {
            var args = new TriggerFiredEventArgs(trigger, () => IsDispatchCurrent(generation));
            if (!args.IsCurrent) { break; }
            TriggerFired?.Invoke(this, args);
        }

        return fired;
    }

    private bool IsDispatchCurrent(long generation)
    {
        lock (_sync) { return !_suspended && generation == _dispatchGeneration; }
    }

    private bool ShouldFire(
        TriggerDefinition trigger,
        DateTimeOffset now,
        TimeSpan stopwatchElapsed,
        DateTimeOffset? previousNow)
    {
        var parameters = trigger.Parameters;
        return trigger.TriggerType switch
        {
            TriggerType.ClockTime => parameters.ClockTime is { } clockTime
                && CrossedClockTime(previousNow, now, clockTime),
            TriggerType.StopwatchElapsed => parameters.Elapsed is { } stopwatchTarget
                && stopwatchElapsed >= stopwatchTarget,
            TriggerType.SoundEnded => parameters.SourceSoundId is { } endedSoundId
                && _soundEndedSinceTick.Contains(endedSoundId),
            TriggerType.SoundElapsed => parameters.SourceSoundId is { } startedSoundId
                && parameters.Elapsed is { } soundTarget
                && _soundStarted.TryGetValue(startedSoundId, out var clock)
                && !clock.Paused
                && clock.Elapsed + _timeProvider.GetElapsedTime(clock.Started) >= soundTarget,
            _ => false
        };
    }

    private static bool CrossedClockTime(
        DateTimeOffset? previousNow,
        DateTimeOffset now,
        TimeOnly target)
    {
        if (previousNow is not { } previous || now <= previous)
        {
            return false;
        }

        var previousAtCurrentOffset = previous.ToOffset(now.Offset);
        var candidate = new DateTimeOffset(previousAtCurrentOffset.Date.AddTicks(target.Ticks), now.Offset);
        if (candidate <= previousAtCurrentOffset)
        {
            candidate = candidate.AddDays(1);
        }

        return candidate <= now;
    }

    private sealed class SoundClock(long started)
    {
        public long Started { get; set; } = started;

        public TimeSpan Elapsed { get; set; }

        public bool Paused { get; set; }
    }
}

public sealed class TriggerFiredEventArgs : EventArgs
{
    private readonly Func<bool>? _isCurrent;

    public TriggerFiredEventArgs(TriggerDefinition trigger)
    {
        Trigger = trigger;
    }

    internal TriggerFiredEventArgs(TriggerDefinition trigger, Func<bool> isCurrent) : this(trigger) => _isCurrent = isCurrent;

    public bool IsCurrent => _isCurrent?.Invoke() ?? true;

    public TriggerDefinition Trigger { get; }
}
