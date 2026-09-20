using PondashiEX.Audio;

namespace PondashiEX.Tests;

public sealed class AudioFileFormatsTests
{
    [Theory]
    [InlineData("sound.wav")]
    [InlineData("sound.mp3")]
    [InlineData("sound.ogg")]
    [InlineData("sound.opus")]
    [InlineData("sound.flac")]
    [InlineData("sound.aif")]
    [InlineData("sound.aiff")]
    [InlineData("sound.m4a")]
    [InlineData("sound.aac")]
    [InlineData("sound.wma")]
    [InlineData("SOUND.FLAC")]
    public void IsSupported_AcceptsDocumentedAudioExtensions(string path)
    {
        Assert.True(AudioFileFormats.IsSupported(path));
    }

    [Fact]
    public void IsSupported_RejectsUnrelatedFiles()
    {
        Assert.False(AudioFileFormats.IsSupported("notes.txt"));
    }
}
