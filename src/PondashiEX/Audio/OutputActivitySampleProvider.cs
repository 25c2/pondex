using NAudio.Wave;

namespace PondashiEX.Audio;

internal sealed class OutputActivitySampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Action<int> _samplesRead;

    public OutputActivitySampleProvider(ISampleProvider source, Action<int> samplesRead)
    {
        _source = source;
        _samplesRead = samplesRead;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read > 0)
        {
            _samplesRead(read);
        }

        return read;
    }
}
