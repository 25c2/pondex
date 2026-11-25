using pondex.Project;

namespace pondex.Trigger;

public sealed class TriggerEngine
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, SoundClock> _soundStarted = [];
    private readonly Dictionary<Guid, int> _soundEndedSinceTick = [];
    private readonly Dictionary<Guid, List<SoundClock>> _finishedClocks = [];
    private readonly HashSet<Guid> _latchedTriggers = [];
    private readonly Dictionary<Guid, (DateOnly Date, TimeOnly Target)> _lastClockFire = [];
    private readonly TimeProvider _timeProvider;
    private IReadOnlyList<TriggerDefinition> _triggers = [];
    private Dictionary<TriggerDefinition, TriggerDefinition> _originals = [];
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
        _finishedClocks.Clear();
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

    internal int SatisfiedStopwatchTriggerCount
    {
        get
        {
            lock (_sync)
            {
                return _triggers.Count(trigger => trigger.Enabled && trigger.TriggerType == TriggerType.StopwatchElapsed
                    && _latchedTriggers.Contains(trigger.Id));
            }
        }
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
            _originals = triggers.Where(trigger => !trigger.TargetGroupOnHold)
                .ToDictionary(Snapshot, trigger => trigger);
            _triggers = _originals.Keys.ToArray();
            _dispatchGeneration++;
            if (!preserveRuntimeState)
            {
                ClearSoundNotifications();
                _latchedTriggers.Clear();
                _lastClockFire.Clear();
                _lastTickNow = null;
                _lastStopwatchElapsed = TimeSpan.Zero;
                _stopwatchHasStarted = false;
                _resumeNeedsBaseline = false;
                _requireFreshSoundStart = false;
                return;
            }

            var triggerIds = triggers
                .Where(trigger => trigger.Enabled && !trigger.TargetGroupOnHold)
                .Select(trigger => trigger.Id)
                .ToHashSet();
            _latchedTriggers.RemoveWhere(triggerId => !triggerIds.Contains(triggerId));
            foreach (var clock in _soundStarted.Values) { clock.Fired.RemoveWhere(id => !triggerIds.Contains(id)); }
            foreach (var removed in _lastClockFire.Keys.Where(id => !_triggers.Any(trigger => trigger.Id == id)).ToArray())
            {
                _lastClockFire.Remove(removed);
            }
        }
    }

    private static TriggerDefinition Snapshot(TriggerDefinition source) => new()
    {
        Id = source.Id,
        Enabled = source.Enabled,
        Memo = source.Memo,
        TriggerType = source.TriggerType,
        Parameters = new TriggerParameters
        {
            ClockTime = source.Parameters.ClockTime,
            Elapsed = source.Parameters.Elapsed,
            SourceSoundId = source.Parameters.SourceSoundId
        },
        Action = source.Action,
        TargetSoundId = source.TargetSoundId,
        TargetGroup = source.TargetGroup
    };

    public void OnSoundStarted(Guid soundId)
    {
        lock (_sync)
        {
            if (_suspended) { return; }
            _soundStarted[soundId] = new SoundClock(_timeProvider.GetTimestamp());
        }
    }

    public void OnSoundEnded(Guid soundId, bool naturalEnd)
    {
        lock (_sync)
        {
            if (_suspended) { return; }
            var wasStarted = _soundStarted.Remove(soundId, out var clock);
            if (!naturalEnd || (_requireFreshSoundStart && !wasStarted)) { return; }
            _soundEndedSinceTick[soundId] = _soundEndedSinceTick.GetValueOrDefault(soundId) + 1;
            if (clock is null) { return; }
            if (!clock.Paused) { clock.Elapsed += _timeProvider.GetElapsedTime(clock.Started); }
            if (!_finishedClocks.TryGetValue(soundId, out var finished))
            {
                finished = [];
                _finishedClocks.Add(soundId, finished);
            }
            finished.Add(clock);
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
        TriggerDefinition[] originalResults;
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
                if (trigger.TriggerType is TriggerType.SoundEnded or TriggerType.SoundElapsed)
                {
                    var count = CountSoundOccurrences(trigger);
                    for (var i = 0; i < count; i++) { fired.Add(trigger); }
                    continue;
                }

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

                if (trigger.TriggerType == TriggerType.ClockTime
                    || _latchedTriggers.Add(trigger.Id))
                {
                    fired.Add(trigger);
                }
            }

            _soundEndedSinceTick.Clear();
            _finishedClocks.Clear();
            originalResults = fired.Select(trigger => _originals[trigger]).ToArray();
            _lastTickNow = now;
        }

        foreach (var trigger in fired)
        {
            var args = new TriggerFiredEventArgs(trigger, () => IsDispatchCurrent(generation));
            if (!args.IsCurrent) { break; }
            TriggerFired?.Invoke(this, args);
        }

        return originalResults;
    }

    private bool IsDispatchCurrent(long generation)
    {
        lock (_sync) { return !_suspended && generation == _dispatchGeneration; }
    }

    private int CountSoundOccurrences(TriggerDefinition trigger)
    {
        if (trigger.Parameters.SourceSoundId is not { } source) { return 0; }
        if (trigger.TriggerType == TriggerType.SoundEnded) { return _soundEndedSinceTick.GetValueOrDefault(source); }
        if (trigger.Parameters.Elapsed is not { } target) { return 0; }

        var count = _finishedClocks.TryGetValue(source, out var finished)
            ? finished.Count(clock => clock.Elapsed >= target && clock.Fired.Add(trigger.Id)) : 0;
        if (_soundStarted.TryGetValue(source, out var active) && !active.Paused
            && active.Elapsed + _timeProvider.GetElapsedTime(active.Started) >= target && active.Fired.Add(trigger.Id))
        {
            count++;
        }
        return count;
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
                && CrossedClockTime(trigger.Id, previousNow, now, clockTime),
            TriggerType.StopwatchElapsed => parameters.Elapsed is { } stopwatchTarget
                && stopwatchElapsed >= stopwatchTarget,
            _ => false
        };
    }

    private bool CrossedClockTime(
        Guid triggerId,
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

        var date = DateOnly.FromDateTime(candidate.Date);
        if (candidate > now || (_lastClockFire.TryGetValue(triggerId, out var previousDate) && target == previousDate.Target && date <= previousDate.Date))
        {
            return false;
        }

        _lastClockFire[triggerId] = (date, target);
        return true;
    }

    private sealed class SoundClock(long started)
    {
        public HashSet<Guid> Fired { get; } = [];

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
