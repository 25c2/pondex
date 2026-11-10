using System.Text.Json;
using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Project;
using PondashiEX.Trigger;

namespace PondashiEX.Tests;

public sealed class LiveSafetyRegressionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmergencySuspension_DropsPendingAndLaterNaturalEnd(bool notificationBeforeStop)
    {
        var source = Guid.NewGuid();
        var engine = new TriggerEngine();
        engine.SetTriggers([new TriggerDefinition
        {
            TriggerType = TriggerType.SoundEnded,
            Parameters = new TriggerParameters { SourceSoundId = source }
        }]);
        if (notificationBeforeStop) { engine.OnSoundEnded(source, true); }
        engine.Suspend();
        if (!notificationBeforeStop) { engine.OnSoundEnded(source, true); }
        Assert.Empty(engine.Tick(DateTimeOffset.Now, TimeSpan.Zero));
        engine.Resume();
        Assert.Empty(engine.Tick(DateTimeOffset.Now, TimeSpan.Zero));
        engine.OnSoundEnded(source, true);
        Assert.Empty(engine.Tick(DateTimeOffset.Now, TimeSpan.Zero));
        engine.OnSoundStarted(source);
        engine.OnSoundEnded(source, true);
        Assert.Single(engine.Tick(DateTimeOffset.Now, TimeSpan.Zero));
    }

    [Fact]
    public void EmergencySuspension_BlocksClockAndStopwatchUntilExplicitResume()
    {
        var now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        var engine = new TriggerEngine();
        engine.SetTriggers([
            new TriggerDefinition { TriggerType = TriggerType.ClockTime,
                Parameters = new TriggerParameters { ClockTime = new TimeOnly(12, 0, 1) } },
            new TriggerDefinition { TriggerType = TriggerType.StopwatchElapsed,
                Parameters = new TriggerParameters { Elapsed = TimeSpan.FromSeconds(1) } }
        ]);
        Assert.Empty(engine.Tick(now, TimeSpan.Zero));
        engine.Suspend();
        Assert.Empty(engine.Tick(now.AddSeconds(2), TimeSpan.FromSeconds(2)));
        engine.Resume();
        // Neither missed clock nor past stopwatch events may replay on resume.
        Assert.Empty(engine.Tick(now.AddSeconds(3), TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void StopwatchZero_WaitsForStartAndDoesNotRearmOnPause()
    {
        var engine = new TriggerEngine();
        engine.SetTriggers([new TriggerDefinition { TriggerType = TriggerType.StopwatchElapsed,
            Parameters = new TriggerParameters { Elapsed = TimeSpan.Zero } }]);
        var now = DateTimeOffset.Now;
        Assert.Empty(engine.Tick(now, TimeSpan.Zero, stopwatchRunning: false));
        Assert.Single(engine.Tick(now, TimeSpan.Zero, stopwatchRunning: true));
        Assert.Empty(engine.Tick(now, TimeSpan.Zero, stopwatchRunning: false));
        Assert.Empty(engine.Tick(now, TimeSpan.Zero, stopwatchRunning: true));
        engine.ResetStopwatch();
        Assert.Empty(engine.Tick(now, TimeSpan.Zero, stopwatchRunning: false));
        Assert.Single(engine.Tick(now, TimeSpan.Zero, stopwatchRunning: true));
    }

    [Fact]
    public async Task PartialLoad_BlocksOriginalEvenThroughSaveAsAndPreservesUnknownFields()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("original.pondex");
        const string original = """
            {"formatVersion":1,"future":{"value":42},"sounds":[
            {"name":"retained","file":"missing.wav","futureSound":17,"loop":{"futureLoop":true}},
            {"volume":"not a number"}]}
            """;
        await File.WriteAllTextAsync(path, original);
        using var manager = new ProjectManager(new ProjectSerializer());
        Assert.True(await manager.OpenAsync(path));
        Assert.NotEmpty(manager.Current.LoadWarnings);
        Assert.Single(manager.Current.MissingFiles);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveAsAsync(path));
        var copy = directory.GetPath("recovered.pondex");
        await manager.SaveAsAsync(copy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveAsAsync(path));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(copy));
        Assert.Equal(42, json.RootElement.GetProperty("future").GetProperty("value").GetInt32());
        var sound = json.RootElement.GetProperty("sounds")[0];
        Assert.Equal(17, sound.GetProperty("futureSound").GetInt32());
        Assert.True(sound.GetProperty("loop").GetProperty("futureLoop").GetBoolean());
    }

    [Fact]
    public void FadeTail_EndsByRenderedSamplesRatherThanWallClock()
    {
        var provider = new FadeTailSampleProvider(new ConstantSource());
        provider.FinishAfter(TimeSpan.FromMilliseconds(100));
        var buffer = new float[10000];
        Assert.Equal(8820, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(0, provider.Read(buffer, 0, buffer.Length));
    }

    private sealed class ConstantSource : ISampleProvider
    {
        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            Array.Fill(buffer, 1F, offset, count);
            return count;
        }
    }
}
