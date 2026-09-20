using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Diagnostics;

namespace PondashiEX.Tests;

public sealed class AudioDecoderFactoryTests
{
    [Fact]
    public void Open_DecodesManagedMp3WithoutWindowsCodec()
    {
        DecodeEmbeddedAudio("tone.mp3", AudioSelfTestFixtures.Mp3Base64);
    }

    [Fact]
    public void Open_DecodesManagedOggVorbisWithoutExternalCodec()
    {
        DecodeEmbeddedAudio("tone.ogg", AudioSelfTestFixtures.OggBase64);
    }

    private static void DecodeEmbeddedAudio(string fileName, string base64)
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath(fileName);
        File.WriteAllBytes(path, Convert.FromBase64String(base64));

        using (var decoder = AudioDecoderFactory.Open(path))
        {
            var provider = decoder.ToSampleProvider();
            var buffer = new float[8_192];
            var read = provider.Read(buffer, 0, buffer.Length);

            Assert.True(read > 0);
            Assert.Contains(buffer.Take(read), sample => Math.Abs(sample) > 0.001f);
        }
    }
}

