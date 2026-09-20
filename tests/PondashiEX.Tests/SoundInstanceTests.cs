using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Project;

namespace PondashiEX.Tests;

public sealed class SoundInstanceTests
{
    [Fact]
    public void Output_WhenFiniteSoundEnds_SignalsCompletionOnce()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("completion.wav");
        WriteSineWave(path);
        var completionCount = 0;
        using var instance = SoundInstance.Create(
            new SoundDefinition { Id = Guid.NewGuid(), Name = "completion" },
            path,
            WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2),
            () => completionCount++);
        var buffer = new float[2_048];

        for (var iteration = 0; iteration < 100 && instance.Output.Read(buffer, 0, buffer.Length) > 0; iteration++)
        {
        }

        Assert.Equal(1, completionCount);
        Assert.Equal(0, instance.Output.Read(buffer, 0, buffer.Length));
        Assert.Equal(1, completionCount);
    }

    [Fact]
    public void Create_WithPitchAndLoop_ProducesFiniteStereoSamples()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("pitch-loop.wav");
        WriteSineWave(path);
        var definition = new SoundDefinition
        {
            Id = Guid.NewGuid(),
            Name = "pitch-loop",
            Pitch = 1.5f,
            Loop = new LoopSettings { Enabled = true, StartSample = 220 }
        };
        using var instance = SoundInstance.Create(
            definition,
            path,
            WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2),
            () => { });
        var buffer = new float[8_192];

        var read = instance.Output.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        Assert.All(buffer.Take(read), sample => Assert.True(float.IsFinite(sample)));
        Assert.Contains(buffer.Take(read), sample => Math.Abs(sample) > 0.001f);
    }

    [Fact]
    public void ApplyLiveSettings_UsesLoopStartSecondsOnNextLoop()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("loop-seconds.wav");
        short[] sourceSamples = [1000, 2000, 3000, 4000];
        using (var writer = new WaveFileWriter(path, new WaveFormat(44_100, 16, 1)))
        {
            foreach (var sample in sourceSamples)
            {
                var bytes = BitConverter.GetBytes(sample);
                writer.Write(bytes, 0, bytes.Length);
            }
        }

        var definition = new SoundDefinition
        {
            Id = Guid.NewGuid(),
            Loop = new LoopSettings { Enabled = true }
        };
        using var instance = SoundInstance.Create(
            definition,
            path,
            WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2),
            () => { });
        definition.Loop.StartSeconds = 2.0 / 44_100;
        instance.ApplyLiveSettings(definition);
        var buffer = new float[12];

        var read = instance.Output.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        Assert.Equal(sourceSamples[0] / 32768.0f, buffer[0], 5);
        Assert.Equal(sourceSamples[3] / 32768.0f, buffer[6], 5);
        Assert.Equal(sourceSamples[2] / 32768.0f, buffer[8], 5);
        Assert.Equal(sourceSamples[3] / 32768.0f, buffer[10], 5);
    }

    private static void WriteSineWave(string path)
    {
        const int sampleRate = 44_100;
        const int sampleCount = sampleRate / 10;
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1));
        for (var index = 0; index < sampleCount; index++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * 440 * index / sampleRate) * short.MaxValue * 0.2);
            var bytes = BitConverter.GetBytes(value);
            writer.Write(bytes, 0, bytes.Length);
        }
    }
}
