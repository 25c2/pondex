using NAudio.Vorbis;
using NAudio.Wave;
using NLayer.NAudioSupport;
using pondex.Project;
using pondex.Utility;

namespace pondex.Audio;

internal static class AudioDecoderFactory
{
    public static WaveStream Open(string path, OperationTiming? timing = null, bool alreadyResolved = false)
    {
        string safePath;
        using (timing?.Measure("decoder_path"))
        {
            safePath = alreadyResolved ? path : ProjectPath.GetSafeFullPath(path, "音源パス");
        }

        using var open = timing?.Measure("decoder_open_init");
        var stream = ProjectPath.OpenVerifiedRead(safePath);
        try
        {
            var decoder = Path.GetExtension(safePath).ToLowerInvariant() switch
            {
                ".wav" => new WaveFileReader(stream),
                ".mp3" => new ManagedMpegStream(stream, closeOnDispose: false),
                ".ogg" => OpenOgg(stream),
                ".opus" => new OpusWaveReader(stream),
                ".aif" or ".aiff" => new AiffFileReader(stream),
                ".flac" or ".m4a" or ".aac" or ".wma" => new StreamMediaFoundationReader(stream),
                _ => throw new NotSupportedException($"未対応の音声形式です: {Path.GetExtension(safePath)}")
            };
            return new OwnedWaveStream(decoder, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static WaveStream OpenOgg(Stream stream)
    {
        var header = OggIdentification.ReadPacket(stream);
        stream.Position = 0;
        if (header.AsSpan().StartsWith("OpusHead"u8)) { return new OpusWaveReader(stream, header); }
        if (header.AsSpan().StartsWith("\x01vorbis"u8)) { return new VorbisWaveReader(stream, closeOnDispose: false); }
        throw new NotSupportedException("Oggコンテナ内の音声形式を判別できません。VorbisまたはOpusを指定してください。");
    }
}
