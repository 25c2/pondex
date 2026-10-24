using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace PondashiEX.Audio;

internal sealed class LivePitchSampleProvider(ISampleProvider source) : ISampleProvider
{
    private readonly object _sync = new();
    private SmbPitchShiftingSampleProvider? _pitchProvider;
    private float _factor = 1F;

    public WaveFormat WaveFormat => source.WaveFormat;

    public float Factor
    {
        set
        {
            lock (_sync)
            {
                _factor = float.IsFinite(value) ? Math.Clamp(value, 0.5F, 2F) : 1F;
            }
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_sync)
        {
            if (_pitchProvider is null && Math.Abs(_factor - 1F) < 0.001F)
            {
                return source.Read(buffer, offset, count);
            }

            _pitchProvider ??= new SmbPitchShiftingSampleProvider(source);
            _pitchProvider.PitchFactor = _factor;
            return _pitchProvider.Read(buffer, offset, count);
        }
    }
}
