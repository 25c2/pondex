using NAudio.Wave;

namespace PondashiEX.Audio;

internal sealed class OwnedWaveStream : WaveStream
{
    private readonly WaveStream _inner;
    private readonly IDisposable _owner;
    private bool _disposed;

    public OwnedWaveStream(WaveStream inner, IDisposable owner)
    {
        _inner = inner;
        _owner = owner;
    }

    public override WaveFormat WaveFormat => _inner.WaveFormat;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        _inner.Read(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try
            {
                _inner.Dispose();
            }
            finally
            {
                _owner.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
