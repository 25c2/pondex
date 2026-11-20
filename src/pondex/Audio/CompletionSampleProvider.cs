using NAudio.Wave;

namespace pondex.Audio;

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

    internal Action<Exception>? ReadFailed { get; set; }

    public long SamplesRead => Interlocked.Read(ref _samplesRead);

    public int Read(float[] buffer, int offset, int count)
    {
        if (Volatile.Read(ref _completionSignaled) != 0) { return 0; }
        int read;
        try { read = _source.Read(buffer, offset, count); }
        catch (Exception exception) when (ReadFailed is not null
            && exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException or FormatException
                or System.Runtime.InteropServices.ExternalException)
        {
            if (Interlocked.Exchange(ref _completionSignaled, 1) == 0) { ReadFailed(exception); }
            return 0;
        }
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
