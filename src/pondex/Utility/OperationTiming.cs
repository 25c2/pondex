using System.Diagnostics;
using System.Globalization;

namespace pondex.Utility;

internal sealed class OperationTiming(long? started = null)
{
    private readonly long _started = started ?? Stopwatch.GetTimestamp();
    private readonly Dictionary<string, TimeSpan> _phases = new(StringComparer.Ordinal);

    internal Guid Id { get; } = Guid.NewGuid();
    internal int Bytes { get; set; }
    internal string Destination { get; set; } = "unknown";
    internal string Outcome { get; set; } = "cancelled";

    internal Phase Measure(string name) => new(this, name, Stopwatch.GetTimestamp());

    internal void Record(string name, long timestamp) =>
        _phases[name] = _phases.GetValueOrDefault(name) + Stopwatch.GetElapsedTime(timestamp);

    internal string Format()
    {
        var phases = string.Join(' ', _phases.Select(phase => string.Create(CultureInfo.InvariantCulture,
            $"{phase.Key}_ms={phase.Value.TotalMilliseconds:F2}")));
        return string.Create(CultureInfo.InvariantCulture,
            $"id={Id:N} outcome={Outcome} total_ms={Stopwatch.GetElapsedTime(_started).TotalMilliseconds:F2} {phases}");
    }

    internal readonly struct Phase(OperationTiming timing, string name, long start) : IDisposable
    {
        public void Dispose() => timing.Record(name, start);
    }
}
