using NAudio.Vorbis;
using NAudio.Wave;
using NLayer.NAudioSupport;
using PondashiEX.Project;

namespace PondashiEX.Audio;

internal static class AudioDecoderFactory
{
    public static WaveStream Open(string path)
    {
        var safePath = ProjectPath.GetSafeFullPath(path, "音源パス");
        if (!File.Exists(safePath))
        {
            throw new FileNotFoundException("音声ファイルが見つかりません。", safePath);
        }

        return Path.GetExtension(safePath).ToLowerInvariant() switch
        {
            ".wav" => OpenOwned(safePath, static stream => new WaveFileReader(stream)),
            ".mp3" => OpenOwned(safePath, static stream => new ManagedMpegStream(stream, closeOnDispose: false)),
            ".ogg" => OpenOgg(safePath),
            ".opus" => new OpusWaveReader(safePath),
            ".aif" or ".aiff" => OpenOwned(safePath, static stream => new AiffFileReader(stream)),
            ".flac" or ".m4a" or ".aac" or ".wma" => new MediaFoundationReader(safePath),
            _ => throw new NotSupportedException($"未対応の音声形式です: {Path.GetExtension(safePath)}")
        };
    }

    private static WaveStream OpenOgg(string path)
    {
        var header = OggIdentification.ReadPacket(path);
        if (header.AsSpan().StartsWith("OpusHead"u8))
        {
            return new OpusWaveReader(path);
        }

        if (!header.AsSpan().StartsWith("\x01vorbis"u8))
        {
            throw new NotSupportedException("Oggコンテナ内の音声形式を判別できません。VorbisまたはOpusを指定してください。");
        }

        return OpenOwned(path, static stream => new VorbisWaveReader(stream, closeOnDispose: false));
    }

    private static OwnedWaveStream OpenOwned(string path, Func<Stream, WaveStream> factory)
    {
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        try
        {
            return new OwnedWaveStream(factory(stream), stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
