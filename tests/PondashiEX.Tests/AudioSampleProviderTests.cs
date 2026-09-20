using NAudio.Wave;
using PondashiEX.Audio;

namespace PondashiEX.Tests;

public sealed class AudioSampleProviderTests
{
    [Fact]
    public void PauseProvider_ReturnsSilenceWithoutConsumingSource()
    {
        var source = new FixedSampleProvider([1.0f, 0.5f, -0.5f, -1.0f]);
        var provider = new PauseSampleProvider(source) { IsPaused = true };
        var buffer = new float[4];

        var pausedRead = provider.Read(buffer, 0, buffer.Length);

        Assert.Equal(4, pausedRead);
        Assert.Equal([0.0f, 0.0f, 0.0f, 0.0f], buffer);
        Assert.Equal(0, source.Position);

        provider.IsPaused = false;
        var resumedRead = provider.Read(buffer, 0, buffer.Length);

        Assert.Equal(4, resumedRead);
        Assert.Equal([1.0f, 0.5f, -0.5f, -1.0f], buffer);
    }

    [Fact]
    public void StereoBalanceProvider_ClampsPanAndAppliesStableFrameGains()
    {
        var source = new FixedSampleProvider([1.0f, 1.0f, 0.5f, 0.25f]);
        var provider = new StereoBalanceSampleProvider(source) { Pan = 2.0f };
        var buffer = new float[4];

        var read = provider.Read(buffer, 0, buffer.Length);

        Assert.Equal(4, read);
        Assert.Equal(1.0f, provider.Pan);
        Assert.Equal([0.0f, 1.0f, 0.0f, 0.25f], buffer);
    }

    private sealed class FixedSampleProvider(float[] samples) : ISampleProvider
    {
        public int Position { get; private set; }

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, samples.Length - Position);
            Array.Copy(samples, Position, buffer, offset, available);
            Position += available;
            return available;
        }
    }
}
