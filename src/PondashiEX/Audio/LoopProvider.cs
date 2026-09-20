using NAudio.Wave;

namespace PondashiEX.Audio;

public sealed class LoopProvider : ISampleProvider
{
    private readonly WaveStream _sourceStream;
    private readonly ISampleProvider _source;
    private long _loopStartPosition;
    private bool _enabled;

    public LoopProvider(WaveStream sourceStream, bool enabled, long startSample)
    {
        _sourceStream = sourceStream ?? throw new ArgumentNullException(nameof(sourceStream));
        _source = sourceStream.ToSampleProvider();
        Configure(enabled, startSample);
    }

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled);
        set => Volatile.Write(ref _enabled, value);
    }

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

            _sourceStream.Position = Volatile.Read(ref _loopStartPosition);
            read = _source.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }

    public void Configure(bool enabled, long startSample)
    {
        var maximumSample = _sourceStream.WaveFormat.BlockAlign == 0
            ? 0
            : _sourceStream.Length / _sourceStream.WaveFormat.BlockAlign;
        // 末尾そのものへシークすると0サンプルでループが終了するため、
        // 音源長以上の指定は安全に先頭へ戻す。
        var safeStartSample = startSample >= maximumSample
            ? 0
            : Math.Max(0, startSample);
        var requestedPosition = safeStartSample * _sourceStream.WaveFormat.BlockAlign;
        Volatile.Write(ref _loopStartPosition, Math.Min(requestedPosition, _sourceStream.Length));
        Enabled = enabled;
    }
}
