using NAudio.Wave;
using NAudio.Dsp;

namespace pondex.Audio;

internal sealed class LivePitchSampleProvider(ISampleProvider source) : ISampleProvider
{
    private const int FftSize = 4096;
    private const int OverSampling = 4;
    private const int BlockFrames = 1024;
    private readonly object _sync = new();
    private SmbPitchShifter[]? _shifters;
    private float[][]? _channels;
    private float[]? _input;
    private float[]? _dryDelay;
    private float[]? _dryBlock;
    private int _dryOffset;
    private float _dryMix;
    private float _factor = 1F;
    private long _inputSamples;
    private long _outputSamples;
    private int _skipFrames = FftSize;
    private int _bufferOffset;
    private int _bufferCount;
    private bool _ended;

    public WaveFormat WaveFormat => source.WaveFormat;

    public float Factor
    {
        set
        {
            lock (_sync)
            {
                _factor = float.IsFinite(value) ? Math.Clamp(value, 0.5F, 2F) : 1F;
                if (Math.Abs(_factor - 1F) >= 0.001F) { EnsureProcessor(); }
            }
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_sync)
        {
            if (_shifters is null && Math.Abs(_factor - 1F) < 0.001F)
            {
                return source.Read(buffer, offset, count);
            }

            EnsureProcessor();
            var written = 0;
            while (written < count)
            {
                if (_bufferOffset >= _bufferCount && !ProcessBlock()) { break; }
                var copy = Math.Min(count - written, _bufferCount - _bufferOffset);
                Array.Copy(_input!, _bufferOffset, buffer, offset + written, copy);
                _bufferOffset += copy;
                written += copy;
            }

            return written;
        }
    }

    private void EnsureProcessor()
    {
        if (_shifters is not null) { return; }
        _shifters = Enumerable.Range(0, WaveFormat.Channels).Select(_ => new SmbPitchShifter()).ToArray();
        _channels = _shifters.Select(_ => new float[BlockFrames]).ToArray();
        _input = new float[BlockFrames * WaveFormat.Channels];
        _dryBlock = new float[_input.Length];
        _dryDelay = new float[FftSize * WaveFormat.Channels];
    }

    private bool ProcessBlock()
    {
        while (true)
        {
            if (_ended && _outputSamples >= _inputSamples) { return false; }
            var input = _input!;
            var read = 0;
            while (!_ended && read < input.Length)
            {
                var next = source.Read(input, read, input.Length - read);
                if (next == 0) { _ended = true; break; }
                read += next;
            }
            _inputSamples += read;
            Array.Clear(input, read, input.Length - read);
            Array.Copy(_dryDelay!, _dryOffset, _dryBlock!, 0, input.Length);
            Array.Copy(input, 0, _dryDelay!, _dryOffset, input.Length);
            _dryOffset = (_dryOffset + input.Length) % _dryDelay!.Length;
            for (var channel = 0; channel < WaveFormat.Channels; channel++)
            {
                var data = _channels![channel];
                for (var frame = 0; frame < BlockFrames; frame++)
                {
                    data[frame] = input[frame * WaveFormat.Channels + channel];
                }
                // Keep the FFT history while transitioning to the delay-aligned dry signal.
                _shifters![channel].PitchShift(_factor, BlockFrames, FftSize,
                    OverSampling, WaveFormat.SampleRate, data);
                for (var frame = 0; frame < BlockFrames; frame++)
                {
                    input[frame * WaveFormat.Channels + channel] = Limit(data[frame]);
                }
            }

            MixDelayAlignedInput(input);
            var skip = Math.Min(_skipFrames, BlockFrames);
            _skipFrames -= skip;
            _bufferOffset = skip * WaveFormat.Channels;
            _bufferCount = input.Length;
            if (_ended)
            {
                _bufferCount = _bufferOffset + (int)Math.Min(
                    _bufferCount - _bufferOffset, _inputSamples - _outputSamples);
            }
            _outputSamples += _bufferCount - _bufferOffset;
            if (_bufferCount > _bufferOffset) { return true; }
        }
    }

    private void MixDelayAlignedInput(float[] processed)
    {
        var step = (Math.Abs(_factor - 1F) < 0.001F ? 1F : -1F) / (2 * BlockFrames);
        if (_dryMix == 0 && step < 0) { return; }
        var channels = WaveFormat.Channels;
        for (var frame = 0; frame < BlockFrames; frame++)
        {
            _dryMix = Math.Clamp(_dryMix + step, 0F, 1F);
            for (var channel = 0; channel < channels; channel++)
            {
                var index = frame * channels + channel;
                processed[index] = processed[index] * (1F - _dryMix) + _dryBlock![index] * _dryMix;
            }
        }
    }

    private static float Limit(float sample)
    {
        // Preserve the soft limiter used by NAudio's original pitch provider.
        var magnitude = Math.Abs(sample);
        return magnitude <= 0.95F ? sample
            : MathF.CopySign(0.95F + 0.05F * MathF.Atan((magnitude - 0.95F) / 0.05F) / (MathF.PI / 2), sample);
    }
}
