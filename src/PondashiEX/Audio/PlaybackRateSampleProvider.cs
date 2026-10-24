using NAudio.Wave;

namespace PondashiEX.Audio;

// Streaming resampling: changing speed also changes pitch, like a tape deck.
internal sealed class PlaybackRateSampleProvider(ISampleProvider source) : ISampleProvider
{
    private readonly float[] _buffer = new float[4096 * source.WaveFormat.Channels];
    private readonly float[] _current = new float[source.WaveFormat.Channels];
    private readonly float[] _next = new float[source.WaveFormat.Channels];
    private int _bufferPosition;
    private int _bufferCount;
    private bool _initialized;
    private bool _hasCurrent;
    private bool _hasNext;
    private double _fraction;
    private float _speed = 1;

    public WaveFormat WaveFormat => source.WaveFormat;

    public float Speed
    {
        get => Volatile.Read(ref _speed);
        set => Volatile.Write(ref _speed, float.IsFinite(value) ? Math.Clamp(value, 0.5F, 2F) : 1F);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var speed = Speed;
        if (!_initialized && speed == 1F)
        {
            return source.Read(buffer, offset, count);
        }

        if (!_initialized)
        {
            _initialized = true;
            _hasCurrent = ReadFrame(_current);
            _hasNext = _hasCurrent && ReadFrame(_next);
        }

        var channels = WaveFormat.Channels;
        var written = 0;
        while (_hasCurrent && written + channels <= count)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var next = _hasNext ? _next[channel] : _current[channel];
                buffer[offset + written + channel] = (float)(_current[channel] + (next - _current[channel]) * _fraction);
            }

            written += channels;
            _fraction += speed;
            while (_fraction >= 1 && _hasCurrent)
            {
                _fraction--;
                _hasCurrent = _hasNext;
                if (_hasCurrent)
                {
                    Array.Copy(_next, _current, channels);
                    _hasNext = ReadFrame(_next);
                }
            }
        }

        return written;
    }

    private bool ReadFrame(float[] frame)
    {
        if (_bufferPosition >= _bufferCount)
        {
            _bufferCount = source.Read(_buffer, 0, _buffer.Length);
            _bufferPosition = 0;
        }

        if (_bufferPosition + frame.Length > _bufferCount)
        {
            return false;
        }

        Array.Copy(_buffer, _bufferPosition, frame, 0, frame.Length);
        _bufferPosition += frame.Length;
        return true;
    }
}
