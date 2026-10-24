using System.Buffers.Binary;
using Concentus.Structs;
using Concentus.Oggfile;
using NAudio.Wave;

namespace PondashiEX.Audio;

internal sealed class OpusWaveReader : WaveStream
{
    private const int SampleRate = 48_000;
    private static ReadOnlySpan<byte> OpusHeaderSignature => "OpusHead"u8;

    private readonly string _path;
    private readonly int _channels;
    private readonly int _preSkipFrames;
    private readonly double _outputGain;
    private readonly long _length;
    private FileStream? _stream;
    private Concentus.Structs.OpusDecoder? _decoder;
    private OpusOggReadStream? _reader;
    private byte[]? _packetBytes;
    private int _packetOffset;
    private long _framesToSkip;
    private long _position;
    private bool _disposed;
    private byte[]? _pendingPacket;

    public OpusWaveReader(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        (_channels, _preSkipFrames, _outputGain) = ReadIdentificationHeader(_path);
        WaveFormat = new WaveFormat(SampleRate, 16, _channels);

        try
        {
            OpenReader();
            var granuleFrames = _reader!.GranuleCount;
            if (granuleFrames <= _preSkipFrames)
            {
                throw new InvalidDataException("Ogg Opus音源の再生時間を取得できません。");
            }

            _length = checked((granuleFrames - _preSkipFrames) * WaveFormat.BlockAlign);
            _framesToSkip = _preSkipFrames;
        }
        catch
        {
            DisposeReader();
            throw;
        }
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

    private static (int Channels, int PreSkipFrames, double OutputGain) ReadIdentificationHeader(string path)
    {
        var buffer = OggIdentification.ReadPacket(path);
        if (buffer.Length < 19 || !buffer.AsSpan().StartsWith(OpusHeaderSignature))
        {
            throw new InvalidDataException("Ogg Opus識別ヘッダーが見つかりません。");
        }

        var channels = buffer[9];
        if (buffer[8] > 15 || channels is < 1 or > 2)
        {
            throw new NotSupportedException($"{channels}チャンネルのOgg Opus音源には未対応です。");
        }

        if (buffer[18] != 0)
        {
            // Families 1/255 can also describe ordinary mono/stereo. Preserve
            // support for those single-stream identity mappings, not multistream.
            if (buffer[18] is not (1 or 255) || buffer.Length < 21 + channels
                || buffer[19] != 1 || buffer[20] != channels - 1
                || buffer[21] != 0 || (channels == 2 && buffer[22] != 1))
            {
                throw new NotSupportedException("このOpusチャンネルマッピングには未対応です。");
            }
        }

        var preSkipFrames = BinaryPrimitives.ReadUInt16LittleEndian(
            buffer.AsSpan(10, sizeof(ushort)));
        var gain = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(16, sizeof(short)));
        return (channels, preSkipFrames, Math.Pow(10, gain / (20.0 * 256)));
    }

    private bool LoadNextPacket()
    {
        while (_pendingPacket is not null || _reader!.HasNextPacket)
        {
            short[]? samples;
            if (_pendingPacket is { } packet)
            {
                _pendingPacket = null;
                var frames = OpusPacketInfo.GetNumSamples(packet, SampleRate);
                samples = new short[checked(frames * _channels)];
                _decoder!.Decode(packet, samples.AsSpan(), frames);
            }
            else
            {
                samples = _reader!.DecodeNextPacket();
            }
            if (samples is null)
            {
                ThrowIfDecodeFailed();
                return false;
            }

            if (_outputGain != 1)
            {
                for (var index = 0; index < samples.Length; index++)
                {
                    samples[index] = (short)Math.Clamp(Math.Round(samples[index] * _outputGain), short.MinValue, short.MaxValue);
                }
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
        _pendingPacket = null;
        _packetOffset = 0;
        return false;
    }

    private void ResetTo(long target)
    {
        DisposeReader();
        _pendingPacket = null;
        _packetBytes = null;
        _packetOffset = 0;
        _position = target;
        if (target >= Length)
        {
            return;
        }

        OpenReader();
        // The library's page seek neither reports the exact packet offset nor refreshes
        // its cached packet. Count raw packet durations instead; only decode the final
        // >=80 ms pre-roll required by RFC 7845, rather than all preceding audio.
        _framesToSkip = checked(target / WaveFormat.BlockAlign + _preSkipFrames);
        while (_framesToSkip > 3840 && _reader!.HasNextPacket)
        {
            var packet = _reader.ReadNextRawPacket();
            if (packet is null) { ThrowIfDecodeFailed(); break; }
            var frames = OpusPacketInfo.GetNumSamples(packet, SampleRate);
            if (frames <= 0) { throw new InvalidDataException("Opusパケットのサンプル数が不正です。"); }
            if (_framesToSkip - frames < 3840)
            {
                _pendingPacket = packet;
                break;
            }
            _framesToSkip -= frames;
        }
    }

    private void OpenReader()
    {
        FileStream? stream = null;
        Concentus.Structs.OpusDecoder? decoder = null;
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
            // The recommended factory probes native DLLs in the working directory.
            // This offline distribution deliberately uses the managed decoder only.
#pragma warning disable CS0618
            decoder = new Concentus.Structs.OpusDecoder(SampleRate, _channels);
#pragma warning restore CS0618
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
