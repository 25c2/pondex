using NAudio.Wave;

namespace pondex.Audio;

// Finish rendering after the configured number of interleaved samples, then
// let the normal completion path wait for the device to consume that tail.
internal sealed class FadeTailSampleProvider(ISampleProvider source) : ISampleProvider
{
    private long _remaining = -1;

    public WaveFormat WaveFormat => source.WaveFormat;

    public void FinishAfter(TimeSpan duration) => Interlocked.Exchange(ref _remaining,
        Math.Max(1, (long)Math.Round(duration.TotalSeconds * WaveFormat.SampleRate)) * WaveFormat.Channels);

    public int Read(float[] buffer, int offset, int count)
    {
        var remaining = Interlocked.Read(ref _remaining);
        var requested = remaining < 0 ? count : (int)Math.Min(count, remaining);
        if (requested == 0) { return 0; }
        var read = source.Read(buffer, offset, requested);
        if (remaining >= 0) { Interlocked.Add(ref _remaining, -read); }
        return read;
    }
}
