using NAudio.Wave;
using PondashiEX.Audio;

namespace PondashiEX.Tests;

public sealed class StereoBalanceSampleProviderTests
{
    [Theory]
    [InlineData(-1.0f, 1.0f, 0.0f)]
    [InlineData(0.0f, 1.0f, 1.0f)]
    [InlineData(1.0f, 0.0f, 1.0f)]
    public void Read_AppliesExpectedStereoPan(float pan, float expectedLeft, float expectedRight)
    {
        var source = new ConstantStereoProvider();
        var provider = new StereoBalanceSampleProvider(source) { Pan = pan };
        var buffer = new float[2];

        var read = provider.Read(buffer, 0, buffer.Length);

        Assert.Equal(2, read);
        Assert.Equal(expectedLeft, buffer[0], 3);
        Assert.Equal(expectedRight, buffer[1], 3);
    }

    private sealed class ConstantStereoProvider : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Fill(buffer, 1.0f, offset, count);
            return count;
        }
    }
}
