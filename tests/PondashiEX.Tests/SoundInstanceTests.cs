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

    [Theory]
    [InlineData(44100, false)]
    [InlineData(48000, false)]
    [InlineData(44100, true)]
    [InlineData(48000, true)]
    public void LoopStartSeconds_UsesSourceRateOnCreationAndLiveUpdate(int sampleRate, bool onCreation)
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("loop-seconds.wav");
        short[] sourceSamples = [1000, 2000, 3000, 4000];
        using (var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1)))
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
            Loop = new LoopSettings { Enabled = true, StartSeconds = onCreation ? 2.0 / sampleRate : 0 }
        };
        using var instance = SoundInstance.Create(
            definition,
            path,
            WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2),
            () => { });
        definition.Loop.StartSeconds = 2.0 / sampleRate;
        instance.ApplyLiveSettings(definition);
        var buffer = new float[12];

        var read = instance.Output.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        Assert.Equal(sourceSamples[0] / 32768.0f, buffer[0], 5);
        Assert.Equal(sourceSamples[3] / 32768.0f, buffer[6], 5);
        Assert.Equal(sourceSamples[2] / 32768.0f, buffer[8], 5);
        Assert.Equal(sourceSamples[3] / 32768.0f, buffer[10], 5);
    }

    [Fact]
    public void ApplyLiveSettings_ChangesSpeedOfExistingPlayback()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("speed.wav");
        WriteSineWave(path);
        var definition = new SoundDefinition();
        using var instance = SoundInstance.Create(definition, path,
            WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2), () => { });
        var buffer = new float[200];
        Assert.Equal(200, instance.Output.Read(buffer, 0, buffer.Length));
        definition.Speed = 2;
        instance.ApplyLiveSettings(definition);
        var remaining = 0;
        int read;
        while ((read = instance.Output.Read(buffer, 0, buffer.Length)) > 0) { remaining += read; }
        Assert.Equal(4310, remaining); // (4410 source frames - 100 already played) / 2 * 2 channels.
    }

    [Fact]
    public void ApplyLiveSettings_ChangesPitchOfExistingPlayback()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("live-pitch.wav");
        WriteSineWave(path);
        var definition = new SoundDefinition { Loop = new LoopSettings { Enabled = true } };
        using var instance = SoundInstance.Create(definition, path,
            WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2), () => { });
        var buffer = new float[44_100];
        instance.Output.Read(buffer, 0, buffer.Length);
        var originalCrossings = CountCrossings(buffer);
        definition.Pitch = 1.5F;
        instance.ApplyLiveSettings(definition);
        instance.Output.Read(buffer, 0, buffer.Length); // Allow the pitch processor to fill its window.
        instance.Output.Read(buffer, 0, buffer.Length);
        var shiftedCrossings = CountCrossings(buffer);
        Assert.InRange(shiftedCrossings / (double)originalCrossings, 1.4, 1.6);
    }

    private static int CountCrossings(float[] samples)
    {
        var crossings = 0;
        for (var index = 2; index < samples.Length; index += 2)
        {
            if (samples[index - 2] <= 0 && samples[index] > 0) { crossings++; }
        }

        return crossings;
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
