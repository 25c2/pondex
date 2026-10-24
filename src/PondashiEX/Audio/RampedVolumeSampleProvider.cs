using NAudio.Wave;

namespace PondashiEX.Audio;

internal sealed class RampedVolumeSampleProvider : ISampleProvider
{
    private readonly object _sync = new();
    private readonly ISampleProvider _source;
    private float _currentVolume;
    private float _targetVolume;
    private float _stepPerFrame;
    private long _framesRemaining;
    private int _samplesUntilNextStep;

    public RampedVolumeSampleProvider(ISampleProvider source, float initialVolume)
    {
        _source = source;
        _currentVolume = Math.Clamp(initialVolume, 0.0f, 1.0f);
        _targetVolume = _currentVolume;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float Volume
    {
        get
        {
            lock (_sync)
            {
                return _targetVolume;
            }
        }
        set
        {
            lock (_sync)
            {
                var target = Math.Clamp(value, 0.0f, 1.0f);
                if (target == _targetVolume) { return; }
                _currentVolume = target;
                _targetVolume = _currentVolume;
                _stepPerFrame = 0;
                _framesRemaining = 0;
                _samplesUntilNextStep = 0;
            }
        }
    }

    public void BeginRamp(float targetVolume, TimeSpan duration)
    {
        lock (_sync)
        {
            _targetVolume = Math.Clamp(targetVolume, 0.0f, 1.0f);
            var frameCount = (long)Math.Round(
                duration.TotalSeconds * WaveFormat.SampleRate,
                MidpointRounding.AwayFromZero);
            if (frameCount <= 0)
            {
                _currentVolume = _targetVolume;
                _stepPerFrame = 0;
                _framesRemaining = 0;
                _samplesUntilNextStep = 0;
                return;
            }

            _framesRemaining = frameCount;
            _stepPerFrame = (_targetVolume - _currentVolume) / frameCount;
            _samplesUntilNextStep = 0;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = _source.Read(buffer, offset, count);
        lock (_sync)
        {
            var end = offset + samplesRead;
            for (var index = offset; index < end; index++)
            {
                if (_framesRemaining > 0 && _samplesUntilNextStep == 0)
                {
                    _currentVolume += _stepPerFrame;
                    _framesRemaining--;
                    if (_framesRemaining == 0)
                    {
                        _currentVolume = _targetVolume;
                    }

                    _samplesUntilNextStep = WaveFormat.Channels;
                }

                buffer[index] *= _currentVolume;
                if (_samplesUntilNextStep > 0)
                {
                    _samplesUntilNextStep--;
                }
            }
        }

        return samplesRead;
    }
}
