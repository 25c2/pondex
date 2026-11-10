using NAudio.Wave;
using pondex.Audio;

namespace pondex.Tests;

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

    [Theory]
    [InlineData(0.5F)]
    [InlineData(1F)]
    [InlineData(1.5F)]
    [InlineData(2F)]
    public void Volume_MultipliesPcmWithoutClampingToUnity(float gain)
    {
        var source = new ConstantSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(100, 2));
        var provider = new RampedVolumeSampleProvider(source, gain);
        var samples = new float[2];
        provider.Read(samples, 0, samples.Length);
        Assert.All(samples, sample => Assert.Equal(gain, sample));
        provider.Volume = 1.75F;
        provider.Read(samples, 0, samples.Length);
        Assert.All(samples, sample => Assert.Equal(1.75F, sample));
    }

    [Fact]
    public void BeginRamp_CanCrossOneHundredPercentAndStopsAtTwoHundredPercent()
    {
        var provider = new RampedVolumeSampleProvider(
            new ConstantSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(100, 1)), 1F);
        var samples = new float[10];
        provider.BeginRamp(3F, TimeSpan.FromMilliseconds(100));
        provider.Read(samples, 0, samples.Length);
        Assert.Equal(1.1F, samples[0], 3);
        Assert.Equal(2F, samples[^1], 3);
        Assert.Equal(2F, provider.Volume);
        Assert.True(samples.Zip(samples.Skip(1), (first, second) => first <= second).All(value => value));
    }

    [Fact]
    public void RemovingBoostPermission_ClampsEvenWhenFadeTargetAlreadyEqualsOne()
    {
        var provider = new RampedVolumeSampleProvider(
            new ConstantSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(100, 1)), 2F);
        provider.BeginRamp(1F, TimeSpan.FromSeconds(1));
        var before = new float[10];
        provider.Read(before, 0, before.Length);
        Assert.True(before[^1] > 1F);
        provider.SetMaximumVolume(1F);
        provider.Volume = 1F;
        var after = new float[10];
        provider.Read(after, 0, after.Length);
        Assert.All(after, sample => Assert.InRange(sample, 0F, 1F));
        provider.BeginRamp(2F, TimeSpan.Zero);
        provider.Read(after, 0, after.Length);
        Assert.All(after, sample => Assert.Equal(1F, sample));
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
