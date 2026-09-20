using PondashiEX.Project;

namespace PondashiEX.Trigger;

public sealed class TriggerEngine
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, long> _soundStarted = [];
    private readonly HashSet<Guid> _soundEndedSinceTick = [];
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
        }
    }

    public void OnSoundStarted(Guid soundId)
    {
        lock (_sync)
        {
            _soundStarted[soundId] = _timeProvider.GetTimestamp();
            _soundEndedSinceTick.Remove(soundId);
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
            fired = _triggers
                .Where(trigger => trigger.Enabled)
                .Where(trigger => ShouldFire(trigger, now, stopwatchElapsed))
                .ToList();

            foreach (var trigger in fired)
            {
                // 再入や遅いアクションでも二重発火しないよう、実行通知より先に無効化する。
                trigger.Enabled = false;
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
