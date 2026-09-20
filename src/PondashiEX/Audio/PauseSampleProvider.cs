using NAudio.Wave;

namespace PondashiEX.Audio;

internal sealed class PauseSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private volatile bool _isPaused;

    public PauseSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public bool IsPaused
    {
        get => _isPaused;
        set => _isPaused = value;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        if (!IsPaused)
        {
            return _source.Read(buffer, offset, count);
        }

        Array.Clear(buffer, offset, count);
        return count;
    }
}
