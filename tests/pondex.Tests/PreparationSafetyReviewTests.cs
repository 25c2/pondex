using NAudio.Wave;
using pondex.Audio;
using pondex.Project;
using pondex.Utility;

namespace pondex.Tests;

public sealed class PreparationSafetyReviewTests
{
    [Fact]
    public async Task RapidRetrigger_OpensOnlyRunningAndLastDecoderAndReleasesEveryStream()
    {
        using var directory = new TestDirectory();
        var path = CreateWave(directory);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prepared = 0;
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output(), prepareSound: (sound, file, format, completed) =>
        {
            var instance = SoundInstance.Create(sound, file, format, completed);
            if (Interlocked.Increment(ref prepared) == 1)
            {
                entered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) { instance.Dispose(); throw new TimeoutException(); }
            }
            return instance;
        });
        var sound = new SoundDefinition();
        var starts = 0;
        engine.PlaybackStarted += (_, _) => starts++;
        var first = engine.PlayAsync(sound, path);
        var rest = new List<Task>();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 199; i++) { rest.Add(engine.PlayAsync(sound, path)); }
        }
        finally { release.Set(); }
        await Task.WhenAll(rest.Prepend(first)).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, prepared);
        Assert.Equal(1, starts);
        Assert.Equal(200, engine.PreparationStatistics.Requested);
        Assert.Equal(2, engine.PreparationStatistics.Completed);
        Assert.Equal(199, engine.PreparationStatistics.Discarded);
        Assert.Equal(1, engine.PreparationStatistics.Peak);
        engine.EmergencyStopAll();
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("device")]
    [InlineData("dispose")]
    [InlineData("cancel")]
    public async Task PendingBurst_CannotReviveAfterInvalidation(string operation)
    {
        using var directory = new TestDirectory();
        var path = CreateWave(directory);
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output(), prepareSound: (sound, file, format, completed) =>
        {
            var instance = SoundInstance.Create(sound, file, format, completed);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) { instance.Dispose(); throw new TimeoutException(); }
            return instance;
        });
        var sound = new SoundDefinition();
        var first = engine.PlayAsync(sound, path, cancellation.Token);
        var pending = new List<Task> { first };
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 20; i++) { pending.Add(engine.PlayAsync(sound, path, cancellation.Token)); }
            switch (operation)
            {
                case "stop": engine.EmergencyStopAll(); break;
                case "device": engine.SelectOutputDevice(3); break;
                case "dispose": engine.Dispose(); break;
                case "cancel": cancellation.Cancel(); break;
            }
        }
        finally { release.Set(); }
        if (operation == "cancel") { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.WhenAll(pending)); }
        else { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.Equal(0, engine.PreparationStatistics.Active);
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task DifferentSounds_CanPrepareConcurrentlyWithinGlobalBound()
    {
        using var directory = new TestDirectory();
        var path = CreateWave(directory);
        using var release = new ManualResetEventSlim();
        var fourEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output(), prepareSound: (sound, file, format, completed) =>
        {
            if (Interlocked.Increment(ref entered) == 4) { fourEntered.TrySetResult(); }
            if (!release.Wait(TimeSpan.FromSeconds(10))) { throw new TimeoutException(); }
            return SoundInstance.Create(sound, file, format, completed);
        });
        var tasks = Enumerable.Range(0, 12).Select(_ => engine.PlayAsync(new SoundDefinition(), path)).ToArray();
        try
        {
            await fourEntered.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(4, Volatile.Read(ref entered));
            Assert.Equal(4, engine.PreparationStatistics.Peak);
        }
        finally { release.Set(); }
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(12, engine.GetActiveSoundIds().Count);
        Assert.Equal(4, engine.PreparationStatistics.Peak);
        engine.StopAll();
    }

    private static string CreateWave(TestDirectory directory)
    {
        var path = directory.GetPath("tone.wav");
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
        for (var i = 0; i < 100; i++) { writer.WriteSample(0.05F); }
        return path;
    }

    private sealed class Logger : IAppLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class Output : IAudioOutput
    {
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add { } remove { } }
        public NAudio.Wave.PlaybackState PlaybackState { get; private set; }
        public void Play() => PlaybackState = NAudio.Wave.PlaybackState.Playing;
        public void Stop() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;
        public void Dispose() => Stop();
    }
}
