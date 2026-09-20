using System.Buffers.Binary;
using Concentus;
using Concentus.Oggfile;
using NAudio.Wave;

namespace PondashiEX.Audio;

internal sealed class OpusWaveReader : WaveStream
{
    private const int SampleRate = 48_000;
    private const int HeaderSearchLength = 4_096;
    private static ReadOnlySpan<byte> OpusHeaderSignature => "OpusHead"u8;

    private readonly string _path;
    private readonly int _channels;
    private readonly int _preSkipFrames;
    private readonly long _length;
    private FileStream? _stream;
    private IOpusDecoder? _decoder;
    private OpusOggReadStream? _reader;
    private byte[]? _packetBytes;
    private int _packetOffset;
    private long _framesToSkip;
    private long _position;
    private bool _disposed;

    public OpusWaveReader(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        (_channels, _preSkipFrames) = ReadIdentificationHeader(_path);
        WaveFormat = new WaveFormat(SampleRate, 16, _channels);

        OpenReader();
        var granuleFrames = _reader!.GranuleCount;
        if (granuleFrames <= _preSkipFrames)
        {
            DisposeReader();
            throw new InvalidDataException("Ogg Opus音源の再生時間を取得できません。");
        }

        _length = checked((granuleFrames - _preSkipFrames) * WaveFormat.BlockAlign);
        _framesToSkip = _preSkipFrames;
    }

    public override WaveFormat WaveFormat { get; }

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var target = Math.Clamp(value, 0, Length);
            target -= target % WaveFormat.BlockAlign;
            if (target == _position)
            {
                return;
            }

            ResetTo(target);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("バッファーの範囲を超えています。");
        }

        var totalRead = 0;
        while (count > 0 && _position < Length)
        {
            if (_packetBytes is null || _packetOffset >= _packetBytes.Length)
            {
                if (!LoadNextPacket())
                {
                    break;
                }
            }

            var available = _packetBytes!.Length - _packetOffset;
            var remaining = Length - _position;
            var toCopy = (int)Math.Min(Math.Min(available, count), remaining);
            Buffer.BlockCopy(_packetBytes, _packetOffset, buffer, offset, toCopy);
            _packetOffset += toCopy;
            offset += toCopy;
            count -= toCopy;
            totalRead += toCopy;
            _position += toCopy;
        }

        return totalRead;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            DisposeReader();
        }

        base.Dispose(disposing);
    }

    private static (int Channels, int PreSkipFrames) ReadIdentificationHeader(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: HeaderSearchLength,
            FileOptions.SequentialScan);
        var buffer = new byte[Math.Min(HeaderSearchLength, checked((int)Math.Min(stream.Length, int.MaxValue)))];
        var read = stream.Read(buffer, 0, buffer.Length);
        var signatureOffset = buffer.AsSpan(0, read).IndexOf(OpusHeaderSignature);
        if (signatureOffset < 0 || signatureOffset + 12 > read)
        {
            throw new InvalidDataException("Ogg Opus識別ヘッダーが見つかりません。");
        }

        var channels = buffer[signatureOffset + 9];
        if (channels is < 1 or > 2)
        {
            throw new NotSupportedException($"{channels}チャンネルのOgg Opus音源には未対応です。");
        }

        var preSkipFrames = BinaryPrimitives.ReadUInt16LittleEndian(
            buffer.AsSpan(signatureOffset + 10, sizeof(ushort)));
        return (channels, preSkipFrames);
    }

    private bool LoadNextPacket()
    {
        while (_reader!.HasNextPacket)
        {
            var samples = _reader.DecodeNextPacket();
            if (samples is null)
            {
                ThrowIfDecodeFailed();
                return false;
            }

            var packetFrames = samples.LongLength / _channels;
            var skippedFrames = Math.Min(_framesToSkip, packetFrames);
            _framesToSkip -= skippedFrames;
            var skippedSamples = checked((int)(skippedFrames * _channels));
            var remainingSamples = samples.Length - skippedSamples;
            if (remainingSamples <= 0)
            {
                continue;
            }

            _packetBytes = new byte[checked(remainingSamples * sizeof(short))];
            Buffer.BlockCopy(
                samples,
                skippedSamples * sizeof(short),
                _packetBytes,
                0,
                _packetBytes.Length);
            _packetOffset = 0;
            return true;
        }

        ThrowIfDecodeFailed();
        _packetBytes = null;
        _packetOffset = 0;
        return false;
    }

    private void ResetTo(long target)
    {
        DisposeReader();
        _packetBytes = null;
        _packetOffset = 0;
        _position = target;
        if (target >= Length)
        {
            return;
        }

        OpenReader();
        if (target == 0)
        {
            _framesToSkip = _preSkipFrames;
            return;
        }

        var targetFrames = target / WaveFormat.BlockAlign;
        var encodedFrames = targetFrames + _preSkipFrames;
        var targetTime = TimeSpan.FromSeconds(encodedFrames / (double)SampleRate);
        _reader!.SeekTo(targetTime);

        // OpusOggReadStream caches the first packet before seeking. Decode and discard it
        // once so the following packet comes from the newly selected Ogg page.
        _ = _reader.DecodeNextPacket();
        ThrowIfDecodeFailed();
        _framesToSkip = 0;
    }

    private void OpenReader()
    {
        FileStream? stream = null;
        IOpusDecoder? decoder = null;
        OpusOggReadStream? reader = null;
        try
        {
            stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.RandomAccess);
            decoder = OpusCodecFactory.CreateDecoder(SampleRate, _channels);
            reader = new OpusOggReadStream(decoder, stream);
            if (!reader.HasNextPacket && !string.IsNullOrWhiteSpace(reader.LastError))
            {
                throw new InvalidDataException($"Ogg Opus音源を初期化できません: {reader.LastError}");
            }

            _stream = stream;
            _decoder = decoder;
            _reader = reader;
        }
        catch
        {
            reader?.Close();
            decoder?.Dispose();
            stream?.Dispose();
            throw;
        }
    }

    private void ThrowIfDecodeFailed()
    {
        if (!string.IsNullOrWhiteSpace(_reader?.LastError))
        {
            throw new InvalidDataException($"Ogg Opus音源をデコードできません: {_reader.LastError}");
        }
    }

    private void DisposeReader()
    {
        _reader?.Close();
        _reader = null;
        _decoder?.Dispose();
        _decoder = null;
        _stream?.Dispose();
        _stream = null;
    }
}
