using NAudio.Wave;
using PondashiEX.Audio;

namespace PondashiEX.Tests;

public sealed class PlaybackRateSampleProviderTests
{
    [Theory]
    [InlineData(0.5F, 16)]
    [InlineData(1F, 8)]
    [InlineData(2F, 4)]
    public void SpeedChangesDurationWithoutMixingChannels(float speed, int expectedFrames)
    {
        var provider = new PlaybackRateSampleProvider(new RampSource(8)) { Speed = speed };
        var buffer = new float[100];
        var count = provider.Read(buffer, 0, buffer.Length);
        Assert.Equal(expectedFrames * 2, count);
        for (var index = 0; index < count; index += 2)
        {
            Assert.Equal(-buffer[index], buffer[index + 1]);
        }

        Assert.Equal(0, provider.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void LiveSpeedChange_ContinuesFromCurrentPosition()
    {
        var provider = new PlaybackRateSampleProvider(new RampSource(40));
        var buffer = new float[8];
        Assert.Equal(8, provider.Read(buffer, 0, 8));
        Assert.Equal(3F, buffer[6]);
        provider.Speed = 2;
        Assert.Equal(8, provider.Read(buffer, 0, 8));
        Assert.Equal(4F, buffer[0]);
        Assert.Equal(10F, buffer[6]);
        provider.Speed = 0.5F;
        Assert.Equal(8, provider.Read(buffer, 0, 8));
        Assert.Equal(12F, buffer[0]);
        Assert.Equal(13.5F, buffer[6]);
    }

    private sealed class RampSource(int frames) : ISampleProvider
    {
        private int _frame;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            var written = 0;
            while (written + 2 <= count && _frame < frames)
            {
                buffer[offset + written++] = _frame;
                buffer[offset + written++] = -_frame;
                _frame++;
            }

            return written;
        }
    }
}
