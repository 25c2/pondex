using NAudio.Wave;

namespace PondashiEX.Audio;

internal sealed class CompletionSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Action _completed;
    private int _completionSignaled;
    private long _samplesRead;

    public CompletionSampleProvider(ISampleProvider source, Action completed)
    {
        _source = source;
        _completed = completed;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public long SamplesRead => Interlocked.Read(ref _samplesRead);

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read > 0)
        {
            Interlocked.Add(ref _samplesRead, read);
        }

        // MixingSampleProvider removes a finite input after its final short read,
        // so waiting for a later zero-length read would miss natural completion.
        if (read < count && Interlocked.Exchange(ref _completionSignaled, 1) == 0)
        {
            _completed();
        }

        return read;
    }
}
