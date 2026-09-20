using PondashiEX.Project;

namespace PondashiEX.Trigger;

public sealed class TriggerEngine
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, long> _soundStarted = [];
    private readonly HashSet<Guid> _soundEndedSinceTick = [];
    private readonly HashSet<Guid> _latchedTriggers = [];
    private readonly TimeProvider _timeProvider;
    private IReadOnlyList<TriggerDefinition> _triggers = [];

    public TriggerEngine(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<TriggerFiredEventArgs>? TriggerFired;

    public void SetTriggers(IReadOnlyList<TriggerDefinition> triggers)
    {
        lock (_sync)
        {
            _triggers = triggers;
            _soundStarted.Clear();
            _soundEndedSinceTick.Clear();
            _latchedTriggers.Clear();
        }
    }

    public void OnSoundStarted(Guid soundId)
    {
        lock (_sync)
        {
            _soundStarted[soundId] = _timeProvider.GetTimestamp();
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
                var shouldFire = ShouldFire(trigger, now, stopwatchElapsed);
                if (!shouldFire)
                {
                    if (trigger.TriggerType is TriggerType.ClockTime or TriggerType.StopwatchElapsed)
                    {
                        _latchedTriggers.Remove(trigger.Id);
                    }

                    continue;
                }

                if (trigger.TriggerType == TriggerType.SoundEnded || _latchedTriggers.Add(trigger.Id))
                {
                    fired.Add(trigger);
                }
            }

            _soundEndedSinceTick.Clear();
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
        TimeSpan stopwatchElapsed)
    {
        var parameters = trigger.Parameters;
        return trigger.TriggerType switch
        {
            TriggerType.ClockTime => parameters.ClockTime is { } clockTime
                && TimeOnly.FromDateTime(now.DateTime) >= clockTime,
            TriggerType.StopwatchElapsed => parameters.Elapsed is { } stopwatchTarget
                && stopwatchElapsed >= stopwatchTarget,
            TriggerType.SoundEnded => parameters.SourceSoundId is { } endedSoundId
                && _soundEndedSinceTick.Contains(endedSoundId),
            TriggerType.SoundElapsed => parameters.SourceSoundId is { } startedSoundId
                && parameters.Elapsed is { } soundTarget
                && _soundStarted.TryGetValue(startedSoundId, out var startedTimestamp)
                && _timeProvider.GetElapsedTime(startedTimestamp) >= soundTarget,
            _ => false
        };
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
