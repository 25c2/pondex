using NAudio.Wave;
using PondashiEX.Audio;

namespace PondashiEX.Tests;

public sealed class LoopProviderTests
{
    [Fact]
    public void Read_AfterEnd_ContinuesFromConfiguredSample()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("loop.wav");
        short[] sourceSamples = [1000, 2000, 3000, 4000];
        using (var writer = new WaveFileWriter(path, new WaveFormat(8_000, 16, 1)))
        {
            foreach (var sample in sourceSamples)
            {
                var bytes = BitConverter.GetBytes(sample);
                writer.Write(bytes, 0, bytes.Length);
            }
        }

        using var reader = new WaveFileReader(path);
        var provider = new LoopProvider(reader, enabled: true, startSample: 2);
        var buffer = new float[6];

        var read = provider.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        Assert.Equal(sourceSamples[0] / 32768.0f, buffer[0], 5);
        Assert.Equal(sourceSamples[1] / 32768.0f, buffer[1], 5);
        Assert.Equal(sourceSamples[2] / 32768.0f, buffer[2], 5);
        Assert.Equal(sourceSamples[3] / 32768.0f, buffer[3], 5);
        Assert.Equal(sourceSamples[2] / 32768.0f, buffer[4], 5);
        Assert.Equal(sourceSamples[3] / 32768.0f, buffer[5], 5);
    }
}
