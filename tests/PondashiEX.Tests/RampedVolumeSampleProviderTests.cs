using NAudio.Wave;
using PondashiEX.Audio;

namespace PondashiEX.Tests;

public sealed class RampedVolumeSampleProviderTests
{
    [Fact]
    public void BeginRamp_ReachesTargetOverRequestedDuration()
    {
        var source = new ConstantSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(100, 1));
        var provider = new RampedVolumeSampleProvider(source, 1.0f);
        var buffer = new float[10];

        provider.BeginRamp(0.0f, TimeSpan.FromMilliseconds(100));
        var read = provider.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        Assert.Equal(0.9f, buffer[0], 3);
        Assert.Equal(0.0f, buffer[^1], 3);
        Assert.True(buffer.Zip(buffer.Skip(1), (left, right) => left >= right).All(value => value));
    }

    [Fact]
    public void BeginRamp_AppliesEqualVolumeToBothChannelsInEachFrame()
    {
        var source = new ConstantSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(100, 2));
        var provider = new RampedVolumeSampleProvider(source, 1.0f);
        var buffer = new float[20];

        provider.BeginRamp(0.0f, TimeSpan.FromMilliseconds(100));
        provider.Read(buffer, 0, buffer.Length);

        for (var index = 0; index < buffer.Length; index += 2)
        {
            Assert.Equal(buffer[index], buffer[index + 1]);
        }
    }

    private sealed class ConstantSampleProvider : ISampleProvider
    {
        public ConstantSampleProvider(WaveFormat waveFormat)
        {
            WaveFormat = waveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Fill(buffer, 1.0f, offset, count);
            return count;
        }
    }
}
