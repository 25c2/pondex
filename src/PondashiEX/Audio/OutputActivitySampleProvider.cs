using NAudio.Wave;

namespace PondashiEX.Audio;

internal sealed class OutputActivitySampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Action<int> _samplesRead;
    private long _readPosition;
    private int _pendingSamples;

    internal long CompletionPosition => Interlocked.Read(ref _readPosition) + Volatile.Read(ref _pendingSamples);

    public OutputActivitySampleProvider(ISampleProvider source, Action<int> samplesRead)
    {
        _source = source;
        _samplesRead = samplesRead;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        Volatile.Write(ref _pendingSamples, count);
        try
        {
            var read = _source.Read(buffer, offset, count);
            if (read > 0)
            {
                Interlocked.Add(ref _readPosition, read);
                _samplesRead(read);
            }

            return read;
        }
        finally
        {
            Volatile.Write(ref _pendingSamples, 0);
        }
    }
}
