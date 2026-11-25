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
        var firstTiming = new OperationTiming();
        var first = engine.StartPlaybackMeasuredAsync(sound, path, null, firstTiming);
        var rest = new List<Task>();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 199; i++) { rest.Add(engine.PlayAsync(sound, path)); }
        }
        finally { release.Set(); }
        await Task.WhenAll(rest.Prepend(first)).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(PlaybackStartResult.Cancelled, await first);
        Assert.Equal("superseded", firstTiming.Outcome);
        Assert.Contains(" discard_dispose_ms=", firstTiming.Format());
        Assert.Contains(" request_cleanup_ms=", firstTiming.Format());
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
    [InlineData("individual")]
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
                case "individual": engine.Stop(sound.Id); break;
                case "device": engine.SelectOutputDevice(3); break;
                case "dispose": engine.Dispose(); break;
                case "cancel": cancellation.Cancel(); break;
            }
            var active = Assert.Single(engine.GetActivePreparations());
            Assert.Equal(sound.Id, active.SoundId);
            Assert.Equal(1, active.Request);
            Assert.True(active.Elapsed >= TimeSpan.Zero);
        }
        finally { release.Set(); }
        if (operation == "cancel") { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.WhenAll(pending)); }
        else { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.Equal(0, engine.PreparationStatistics.Active);
        Assert.Empty(engine.GetActivePreparations());
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task ProjectPlayback_PathProbeDoesNotStopCurrentAudioAndCannotReviveAfterEmergency()
    {
        using var directory = new TestDirectory();
        var path = CreateWave(directory);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output());
        var background = new SoundDefinition();
        await engine.PlayAsync(background, path);
        var cue = new SoundDefinition();
        var pending = engine.StartProjectPlaybackAsync(cue, path, () =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) { throw new TimeoutException(); }
            return path;
        }, null, null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(background.Id, engine.GetActiveSoundIds());
            await engine.RequestEmergencyStopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(engine.GetActiveSoundIds());
        }
        finally { release.Set(); }
        Assert.Equal(PlaybackStartResult.Cancelled, await pending);
        Assert.Empty(engine.GetActiveSoundIds());
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
            Assert.Equal(4, engine.GetActivePreparations().Count);
        }
        finally { release.Set(); }
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(12, engine.GetActiveSoundIds().Count);
        Assert.Equal(4, engine.PreparationStatistics.Peak);
        engine.StopAll();
    }

    [Fact]
    public async Task Playback_RejectsAnUnverifiedOutputAfterPreparationWithoutOpeningIt()
    {
        using var directory = new TestDirectory();
        var path = CreateWave(directory);
        var opens = 0;
        using var engine = new AudioEngine(new Logger(), (_, _) => { opens++; return new Output(); });
        var sound = new SoundDefinition();
        var refused = new OperationTiming();
        var result = await engine.StartPlaybackMeasuredAsync(sound, path,
            _ => Task.FromException(new IOException("Output identity changed")), refused);
        Assert.Equal(PlaybackStartResult.Failed, result);
        Assert.Equal("failed_IOException", refused.Outcome);
        Assert.Contains(" recheck_output_ms=", refused.Format());
        Assert.Equal(0, opens);
        Assert.Empty(engine.GetActiveSoundIds());

        result = await engine.StartPlaybackAsync(sound, directory.GetPath("missing.wav"));
        Assert.Equal(PlaybackStartResult.Missing, result);
        Assert.Equal(0, opens);
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
