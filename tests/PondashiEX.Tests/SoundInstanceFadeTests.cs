using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Project;

namespace PondashiEX.Tests;

public sealed class SoundInstanceFadeTests
{
    [Fact]
    public void FadeIn_RaisesPcmAmplitudeAcrossConfiguredDuration()
    {
        using var directory = new TestDirectory();
        var path = CreateConstantWave(directory.GetPath("fade-in.wav"));
        var definition = new SoundDefinition
        {
            FadeIn = new FadeSettings { Enabled = true, Value = 0.1f }
        };
        using var instance = SoundInstance.Create(
            definition,
            path,
            WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2),
            () => { });
        var samples = ReadSamples(instance.Output, 8_820);

        Assert.True(AverageAmplitude(samples, 0, 400) < 0.05f);
        Assert.True(AverageAmplitude(samples, samples.Length - 400, 400) > 0.35f);
    }

    [Fact]
    public void FadeOut_LowersPcmAmplitudeAcrossConfiguredDuration()
    {
        using var directory = new TestDirectory();
        var path = CreateConstantWave(directory.GetPath("fade-out.wav"));
        var definition = new SoundDefinition();
        using var instance = SoundInstance.Create(
            definition,
            path,
            WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2),
            () => { });
        instance.BeginFadeOut(TimeSpan.FromMilliseconds(100));
        var samples = ReadSamples(instance.Output, 8_820);

        Assert.True(AverageAmplitude(samples, 0, 400) > 0.35f);
        Assert.True(AverageAmplitude(samples, samples.Length - 400, 400) < 0.05f);
    }

    private static float[] ReadSamples(ISampleProvider provider, int count)
    {
        var samples = new float[count];
        var read = provider.Read(samples, 0, samples.Length);
        Assert.Equal(samples.Length, read);
        return samples;
    }

    private static float AverageAmplitude(float[] samples, int offset, int count) =>
        samples.Skip(offset).Take(count).Average(Math.Abs);

    private static string CreateConstantWave(string path)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(44_100, 16, 1));
        var sample = BitConverter.GetBytes((short)(short.MaxValue / 2));
        for (var index = 0; index < 44_100; index++)
        {
            writer.Write(sample, 0, sample.Length);
        }

        return path;
    }
}
