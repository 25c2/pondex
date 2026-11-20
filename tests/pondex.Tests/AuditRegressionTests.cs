using NAudio.Wave;
using pondex.Audio;
using pondex.Diagnostics;
using pondex.Project;
using pondex.Trigger;
using pondex.Utility;

namespace pondex.Tests;

public sealed class AuditRegressionTests
{
    [Fact]
    public async Task ReadingOneBrokenSourceDoesNotStopAnotherSourceOrProduceNaturalEnd()
    {
        ISampleProvider? mixer = null;
        using var engine = new AudioEngine(new Logger(), (_, input) =>
        {
            mixer = input;
            return new Output();
        }, prepareSound: (sound, _, format, complete) =>
            SoundInstance.Create(sound, new TestWave(sound.Name == "broken"), format, complete));
        var normal = new SoundDefinition { Name = "BGM" };
        var broken = new SoundDefinition { Name = "broken" };
        var ended = new TaskCompletionSource<PlaybackEndReason?>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackEnded += (_, args) => { if (args.SoundId == broken.Id) { ended.TrySetResult(args.EndReason); } };
        await engine.PlayAsync(normal, "unused.wav");
        await engine.PlayAsync(broken, "unused.wav");

        var buffer = new float[4096];
        mixer!.Read(buffer, 0, buffer.Length);
        mixer.Read(buffer, 0, buffer.Length);
        Assert.Equal(PlaybackEndReason.Error, await ended.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(normal.Id, Assert.Single(engine.GetActiveSoundIds()));
        Assert.False(engine.IsOutputFaulted);
        Assert.Equal(buffer.Length, mixer.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, sample => Assert.Equal(0.125f, sample));
    }

    [Fact]
    public async Task EmergencyStopStillDisposesOutputWhenResetThrows()
    {
        var output = new Output { ThrowOnStop = true };
        using var engine = new AudioEngine(new Logger(), (_, _) => output, prepareSound: (sound, _, format, complete) =>
            SoundInstance.Create(sound, new TestWave(false), format, complete));
        var sound = new SoundDefinition();
        await engine.PlayAsync(sound, "unused.wav");
        engine.EmergencyStopAll();

        Assert.True(output.Disposed);
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.True(engine.IsOutputFaulted);
        await engine.PlayAsync(sound, "unused.wav");
        Assert.Empty(engine.GetActiveSoundIds());
    }

    [Fact]
    public async Task FailingStartSubscriberDoesNotDiscardThePlayingInstance()
    {
        using var engine = new AudioEngine(new Logger(), (_, _) => new Output(), prepareSound: (sound, _, format, complete) =>
            SoundInstance.Create(sound, new TestWave(false), format, complete));
        var sound = new SoundDefinition();
        var observedByOtherSubscriber = false;
        engine.PlaybackStarted += (_, _) => throw new InvalidOperationException("observer failure");
        engine.PlaybackStarted += (_, _) => observedByOtherSubscriber = true;
        Assert.Equal(PlaybackStartResult.Started, await engine.StartPlaybackAsync(sound, "unused.wav"));
        Assert.True(observedByOtherSubscriber);
        Assert.Equal(sound.Id, Assert.Single(engine.GetActiveSoundIds()));
        engine.StopImmediately(sound.Id);
        Assert.Empty(engine.GetActiveSoundIds());
    }

    [Fact]
    public void FinishedPlaysSurviveImmediateRetriggersAndUseFinalElapsedTime()
    {
        var time = new TestTime();
        var engine = new TriggerEngine(time);
        var source = Guid.NewGuid();
        var ended = new TriggerDefinition { TriggerType = TriggerType.SoundEnded, Parameters = new() { SourceSoundId = source } };
        var elapsed = new TriggerDefinition
        {
            TriggerType = TriggerType.SoundElapsed,
            Parameters = new() { SourceSoundId = source, Elapsed = TimeSpan.FromMilliseconds(25) }
        };
        engine.SetTriggers([ended, elapsed]);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            engine.OnSoundStarted(source);
            time.Timestamp += TimeSpan.FromMilliseconds(40).Ticks;
            engine.OnSoundEnded(source, naturalEnd: true);
        }
        engine.OnSoundStarted(source);
        Assert.Equal(new[] { ended, ended, elapsed, elapsed }, engine.Tick(DateTimeOffset.UnixEpoch, TimeSpan.Zero));
        Assert.Empty(engine.Tick(DateTimeOffset.UnixEpoch, TimeSpan.Zero));
    }

    [Fact]
    public void TriggerDispatchUsesSnapshotOfDefinitionsAndParameters()
    {
        var engine = new TriggerEngine();
        var definition = new TriggerDefinition
        {
            TriggerType = TriggerType.StopwatchElapsed,
            Action = TriggerAction.Stop,
            Parameters = new() { Elapsed = TimeSpan.Zero }
        };
        var definitions = new List<TriggerDefinition> { definition };
        engine.SetTriggers(definitions);
        TriggerDefinition? dispatched = null;
        engine.TriggerFired += (_, args) => dispatched = args.Trigger;
        definitions.Clear();
        definition.Action = TriggerAction.Play;
        definition.Parameters.Elapsed = TimeSpan.FromDays(1);
        engine.Tick(DateTimeOffset.UnixEpoch, TimeSpan.Zero);
        Assert.Equal(TriggerAction.Stop, dispatched?.Action);
    }

    [Fact]
    public async Task StaleProjectCannotReplaceMoreRecentSaveAndCanSaveACopy()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("shared.pondex");
        var serializer = new ProjectSerializer();
        await serializer.SaveAsync(path, new ProjectModel());
        using var first = new ProjectManager(serializer);
        using var second = new ProjectManager(serializer);
        Assert.True(await first.OpenAsync(path));
        Assert.True(await second.OpenAsync(path));
        first.Current.Project.Name = "first save";
        first.MarkDirty();
        await first.SaveAsync();
        second.Current.Project.Name = "uncommitted copy";
        second.MarkDirty();

        await Assert.ThrowsAsync<IOException>(() => second.SaveAsync());
        Assert.True(second.IsDirty);
        Assert.Equal("first save", (await serializer.LoadAsync(path)).Project.Name);
        var copy = directory.GetPath("copy.pondex");
        await second.SaveAsAsync(copy);
        Assert.Equal("uncommitted copy", (await serializer.LoadAsync(copy)).Project.Name);
    }

    [Fact]
    public async Task ChangedLinkTargetMustBeSelectedAgainBeforePlayback()
    {
        using var directory = new TestDirectory();
        var original = Path.GetDirectoryName(directory.GetPath("original/alert.wav"))!;
        var changed = Path.GetDirectoryName(directory.GetPath("changed/alert.wav"))!;
        File.WriteAllText(Path.Combine(original, "alert.wav"), "original");
        File.WriteAllText(Path.Combine(changed, "alert.wav"), "changed");
        var link = directory.GetPath("linked");
        CreateLink(link, original);
        try
        {
            var path = directory.GetPath("show.pondex");
            var collection = new ProjectModel { Sounds = [new SoundDefinition { File = "linked/alert.wav" }] };
            var serializer = new ProjectSerializer();
            await serializer.SaveAsync(path, collection);
            using var manager = new ProjectManager(serializer);
            Assert.True(await manager.OpenAsync(path));
            var sound = Assert.Single(manager.Current.Sounds);
            Assert.Equal(Path.Combine(original, "alert.wav"), manager.ResolvePlaybackPath(sound));
            Directory.Delete(link);
            CreateLink(link, changed);
            Assert.Throws<InvalidOperationException>(() => manager.ResolvePlaybackPath(sound));
            manager.ReplaceSoundFile(sound, Path.Combine(changed, "alert.wav"));
            Assert.Equal(Path.Combine(changed, "alert.wav"), manager.ResolvePlaybackPath(sound));
        }
        finally
        {
            if (Directory.Exists(link)) { Directory.Delete(link); }
        }
    }

    private static void CreateLink(string path, string destination)
    {
        if (OperatingSystem.IsWindows()) { TestLinks.CreateJunction(path, destination); }
        else { Directory.CreateSymbolicLink(path, destination); }
    }

    [Fact]
    public async Task AmbiguousTargetsAndUnknownActionsAreDisabledForRecovery()
    {
        using var directory = new TestDirectory();
        var id = Guid.NewGuid();
        var unique = Guid.NewGuid();
        var path = directory.GetPath("bad.pondex");
        await File.WriteAllTextAsync(path, $$"""
            {"formatVersion":1,"sounds":[{"id":"{{id}}"},{"id":"{{id}}"},{"id":"{{unique}}"}],"triggers":[
              {"memo":"ambiguous","targetSoundId":"{{id}}","action":0},
              {"memo":"unknown","targetSoundId":"{{unique}}","action":999},
              {"memo":"valid","targetSoundId":"{{unique}}","action":0}]}
            """);
        var serializer = new ProjectSerializer();
        using var manager = new ProjectManager(serializer);
        Assert.True(await manager.OpenAsync(path));
        var model = manager.Current;
        Assert.Equal(3, model.Sounds.Select(sound => sound.Id).Distinct().Count());
        Assert.False(model.Triggers[0].Enabled);
        Assert.Equal(Guid.Empty, model.Triggers[0].TargetSoundId);
        Assert.False(model.Triggers[1].Enabled);
        Assert.Equal((TriggerAction)999, model.Triggers[1].Action);
        Assert.True(model.Triggers[2].Enabled);
        Assert.True(manager.RequiresSaveAs);
    }

    [Fact]
    public void IntegerVolumePercentagesRoundTrip()
    {
        for (var percent = 0; percent <= 200; percent++)
        {
            Assert.Equal(percent, ProjectLimits.ToVolumePercent(percent / 100F, 2F));
        }
    }

    [Theory]
    [InlineData(215.33203125)]
    [InlineData(440)]
    [InlineData(1000)]
    public void ReturningToUnityRestoresUnmodifiedDelayedAudio(double frequency)
    {
        var source = new Tone(frequency);
        var pitch = new LivePitchSampleProvider(source) { Factor = 1.25f };
        var buffer = new float[1024];
        for (var i = 0; i < 8; i++) { Assert.Equal(buffer.Length, pitch.Read(buffer, 0, buffer.Length)); }
        pitch.Factor = 1;
        for (var i = 0; i < 4; i++) { Assert.Equal(buffer.Length, pitch.Read(buffer, 0, buffer.Length)); }
        for (var i = 0; i < buffer.Length; i++)
        {
            Assert.Equal(source.At(11 * 1024 + i), buffer[i], 6);
        }
    }

    [Fact]
    public void DiagnosticReportsDoNotOverwritePreviousOutput()
    {
        using var directory = new TestDirectory();
        var old = directory.GetPath("audio-self-test.txt");
        File.WriteAllText(old, "previous file");
        var first = DiagnosticReport.Write(directory.Path, "audio-self-test", "first");
        var second = DiagnosticReport.Write(directory.Path, "audio-self-test", "second");
        Assert.NotEqual(first, second);
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("second", File.ReadAllText(second));
        Assert.Equal("previous file", File.ReadAllText(old));
    }

    [Fact]
    public async Task TwoLogWritersUseDifferentSegments()
    {
        using var directory = new TestDirectory();
        var date = new DateOnly(2026, 11, 20);
        await using var first = new BoundedLogWriter(directory.Path, date);
        await using var second = new BoundedLogWriter(directory.Path, date);
        Assert.NotEqual(first.CurrentPath, second.CurrentPath);
        await first.WriteLineAsync("first");
        await second.WriteLineAsync("second");
        await first.DisposeAsync();
        await second.DisposeAsync();
        Assert.Equal("first", Assert.Single(File.ReadAllLines(first.CurrentPath!)));
        Assert.Equal("second", Assert.Single(File.ReadAllLines(second.CurrentPath!)));
    }

    [Fact]
    public void SavingNewHistoryWhileOldProjectIsMissingPreservesItsEntry()
    {
        using var directory = new TestDirectory();
        var store = new RecentProjectStore(directory.GetPath("history.json"));
        var first = directory.GetPath("first.pondex");
        var second = directory.GetPath("second.pondex");
        File.WriteAllText(first, "{}");
        store.Add(first);
        File.Delete(first);
        File.WriteAllText(second, "{}");
        store.Add(second);
        Assert.Equal(second, Assert.Single(store.Load()));
        File.WriteAllText(first, "{}");
        Assert.Equal(new[] { second, first }, store.Load());
    }

    private sealed class TestTime : TimeProvider
    {
        public long Timestamp { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Timestamp;
    }

    private sealed class TestWave(bool fail) : WaveStream
    {
        private int _reads;
        public override WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        public override long Length => 1_000_000_000;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (fail && ++_reads > 1) { throw new InvalidDataException("broken source"); }
            for (var i = 0; i < count; i += sizeof(float))
            {
                BitConverter.TryWriteBytes(buffer.AsSpan(offset + i, sizeof(float)), 0.125f);
            }
            Position += count;
            return count;
        }
    }

    private sealed class Tone(double frequency) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        public float At(int sample) => 0.1f * (float)Math.Sin(sample * (2 * Math.PI * frequency / 44100));
        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++) { buffer[offset + i] = At(_position++); }
            return count;
        }
    }

    private sealed class Output : IAudioOutput
    {
        public bool ThrowOnStop { get; init; }
        public bool Disposed { get; private set; }
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add { } remove { } }
        public NAudio.Wave.PlaybackState PlaybackState { get; private set; }
        public void Play() => PlaybackState = NAudio.Wave.PlaybackState.Playing;
        public void Stop()
        {
            if (ThrowOnStop) { throw new IOException("reset failed"); }
            PlaybackState = NAudio.Wave.PlaybackState.Stopped;
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class Logger : IAppLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
