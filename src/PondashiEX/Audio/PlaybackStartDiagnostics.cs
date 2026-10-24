using System.Diagnostics;

namespace PondashiEX.Audio;

public sealed record PlaybackStartTiming(Guid SoundId, int Attempt, double PreparationMilliseconds, double StartMilliseconds);

internal sealed class PlaybackStartDiagnostics
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, int> _attempts = [];
    private readonly Queue<PlaybackStartTiming> _recent = new();
    private volatile bool _enabled;
    private long _session;

    public bool Enabled
    {
        get { lock (_sync) { return _enabled; } }
        set
        {
            lock (_sync)
            {
                if (_enabled == value) { return; }
                _enabled = value;
                _session++;
                if (value)
                {
                    _attempts.Clear();
                    _recent.Clear();
                }
            }
        }
    }

    public Request Begin(Guid soundId)
    {
        if (!_enabled) { return default; }
        lock (_sync)
        {
            if (!_enabled) { return default; }
            // Bound diagnostic bookkeeping even if projects are repeatedly replaced.
            if (_attempts.Count >= 1024 && !_attempts.ContainsKey(soundId)) { _attempts.Clear(); }
            var attempt = _attempts.GetValueOrDefault(soundId) + 1;
            _attempts[soundId] = attempt;
            return new(_session, Stopwatch.GetTimestamp(), attempt);
        }
    }

    public PlaybackStartTiming? Complete(Guid soundId, Request request, long preparedAt, long startedAt)
    {
        if (request.Attempt == 0) { return null; }
        lock (_sync)
        {
            if (!_enabled || request.Session != _session) { return null; }
            var result = new PlaybackStartTiming(soundId, request.Attempt,
                Stopwatch.GetElapsedTime(request.RequestedAt, preparedAt).TotalMilliseconds,
                Stopwatch.GetElapsedTime(request.RequestedAt, startedAt).TotalMilliseconds);
            if (_recent.Count == 128) { _recent.Dequeue(); }
            _recent.Enqueue(result);
            return result;
        }
    }

    public IReadOnlyList<PlaybackStartTiming> Snapshot()
    {
        lock (_sync) { return _recent.ToArray(); }
    }

    internal readonly record struct Request(long Session, long RequestedAt, int Attempt);
}
