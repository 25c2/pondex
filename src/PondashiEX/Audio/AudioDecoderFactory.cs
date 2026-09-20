using NAudio.Vorbis;
using NAudio.Wave;
using NLayer.NAudioSupport;

namespace PondashiEX.Audio;

internal static class AudioDecoderFactory
{
    public static WaveStream Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("音声ファイルが見つかりません。", path);
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => OpenOwned(path, static stream => new WaveFileReader(stream)),
            ".mp3" => OpenOwned(path, static stream => new ManagedMpegStream(stream, closeOnDispose: false)),
            ".ogg" => OpenOgg(path),
            ".opus" => new OpusWaveReader(path),
            ".aif" or ".aiff" => OpenOwned(path, static stream => new AiffFileReader(stream)),
            ".flac" or ".m4a" or ".aac" or ".wma" => new MediaFoundationReader(path),
            _ => throw new NotSupportedException($"未対応の音声形式です: {Path.GetExtension(path)}")
        };
    }

    private static WaveStream OpenOgg(string path)
    {
        var header = new byte[4_096];
        using (var stream = new FileStream(
                   path,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read | FileShare.Delete,
                   bufferSize: header.Length,
                   FileOptions.SequentialScan))
        {
            var read = stream.Read(header, 0, header.Length);
            var contents = header.AsSpan(0, read);
            if (contents.IndexOf("OpusHead"u8) >= 0)
            {
                return new OpusWaveReader(path);
            }

            if (contents.IndexOf("\x01vorbis"u8) < 0)
            {
                throw new NotSupportedException("Oggコンテナ内の音声形式を判別できません。VorbisまたはOpusを指定してください。");
            }
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
