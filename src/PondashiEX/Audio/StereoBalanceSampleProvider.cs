using NAudio.Wave;

namespace PondashiEX.Audio;

internal sealed class StereoBalanceSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float _pan;

    public StereoBalanceSampleProvider(ISampleProvider source)
    {
        if (source.WaveFormat.Channels != 2)
        {
            throw new ArgumentException("ステレオ音源のみ指定できます。", nameof(source));
        }

        _source = source;
    }

    public float Pan
    {
        get => Volatile.Read(ref _pan);
        set => Volatile.Write(ref _pan, Math.Clamp(value, -1.0f, 1.0f));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var pan = Pan;
        var leftGain = pan <= 0 ? 1.0f : 1.0f - pan;
        var rightGain = pan >= 0 ? 1.0f : 1.0f + pan;
        for (var index = offset; index + 1 < offset + read; index += 2)
        {
            buffer[index] *= leftGain;
            buffer[index + 1] *= rightGain;
        }

        return read;
    }
}
