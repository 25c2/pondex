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

    [Theory]
    [InlineData("tone.ogg")]
    [InlineData("tone.opus")]
    public void Open_DecodesManagedOggOpusWithoutExternalCodec(string fileName)
    {
        DecodeEmbeddedAudio(fileName, AudioSelfTestFixtures.OpusBase64);
    }

    [Fact]
    public void Open_OggOpusCanRewindForLooping()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("tone.ogg");
        File.WriteAllBytes(path, Convert.FromBase64String(AudioSelfTestFixtures.OpusBase64));

        using var decoder = AudioDecoderFactory.Open(path);
        var first = new byte[4_096];
        var firstRead = decoder.Read(first, 0, first.Length);
        decoder.Position = 0;
        var repeated = new byte[4_096];
        var repeatedRead = decoder.Read(repeated, 0, repeated.Length);

        Assert.Equal(firstRead, repeatedRead);
        Assert.Equal(first.AsSpan(0, firstRead), repeated.AsSpan(0, repeatedRead));
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
