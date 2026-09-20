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
            ".ogg" => OpenOwned(path, static stream => new VorbisWaveReader(stream, closeOnDispose: false)),
            _ => throw new NotSupportedException($"未対応の音声形式です: {Path.GetExtension(path)}")
        };
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
