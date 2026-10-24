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
            if (!preserveRuntimeState)
            {
                _soundStarted.Clear();
                _soundEndedSinceTick.Clear();
                _latchedTriggers.Clear();
                _lastTickNow = null;
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
            _soundStarted.Remove(soundId);
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
        TimeSpan stopwatchElapsed)
    {
        List<TriggerDefinition> fired;
        lock (_sync)
        {
            fired = [];
            foreach (var trigger in _triggers.Where(trigger => trigger.Enabled))
            {
                var shouldFire = ShouldFire(trigger, now, stopwatchElapsed, _lastTickNow);
                if (!shouldFire)
                {
                    if (trigger.TriggerType is TriggerType.ClockTime or TriggerType.StopwatchElapsed)
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
            TriggerFired?.Invoke(this, new TriggerFiredEventArgs(trigger));
        }

        return fired;
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
        var candidate = new DateTimeOffset(
            previousAtCurrentOffset.Year,
            previousAtCurrentOffset.Month,
            previousAtCurrentOffset.Day,
            target.Hour,
            target.Minute,
            target.Second,
            target.Millisecond,
            now.Offset);
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
    public TriggerFiredEventArgs(TriggerDefinition trigger)
    {
        Trigger = trigger;
    }

    public TriggerDefinition Trigger { get; }
}
