using NAudio.Wave;

namespace PondashiEX.Audio;

public sealed class LoopProvider : ISampleProvider
{
    private readonly WaveStream _sourceStream;
    private readonly ISampleProvider _source;
    private readonly long _loopStartPosition;

    public LoopProvider(WaveStream sourceStream, bool enabled, long startSample)
    {
        _sourceStream = sourceStream ?? throw new ArgumentNullException(nameof(sourceStream));
        _source = sourceStream.ToSampleProvider();
        Enabled = enabled;

        var maximumSample = sourceStream.WaveFormat.BlockAlign == 0
            ? 0
            : sourceStream.Length / sourceStream.WaveFormat.BlockAlign;
        var safeStartSample = Math.Clamp(startSample, 0, maximumSample);
        var requestedPosition = safeStartSample * sourceStream.WaveFormat.BlockAlign;
        _loopStartPosition = Math.Min(requestedPosition, sourceStream.Length);
    }

    public bool Enabled { get; set; }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = _source.Read(buffer, offset + totalRead, count - totalRead);
            if (read > 0)
            {
                totalRead += read;
                continue;
            }

            if (!Enabled || _sourceStream.Length == 0)
            {
                break;
            }

            _sourceStream.Position = _loopStartPosition;
            read = _source.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }
}
