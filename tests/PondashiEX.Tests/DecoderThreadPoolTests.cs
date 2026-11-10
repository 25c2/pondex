using System.Runtime.ExceptionServices;
using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Project;
using PondashiEX.Utility;

namespace PondashiEX.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DecoderThreadPoolGroup
{
    public const string Name = "Decoder thread pool isolation";
}

[Collection(DecoderThreadPoolGroup.Name)]
public sealed class DecoderThreadPoolTests
{
    [Fact]
    public void BlockedDecoder_DoesNotStarveFadeCompletionOnSharedThreadPool()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunCase(); }
            catch (Exception exception) { failure = exception; }
        })
        { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Decoder isolation test timed out");
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    private static void RunCase()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("tone.wav");
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
        { for (var i = 0; i < 100; i++) { writer.WriteSample(0.05F); } }
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var ended = new ManualResetEventSlim();
        var fading = new SoundDefinition
        {
            Loop = new LoopSettings { Enabled = true },
            FadeOut = new FadeSettings { Enabled = true, Value = 0.01F }
        };
        ISampleProvider? provider = null;
        var output = new Output();
        using var engine = new AudioEngine(new Logger(), (_, input) => { provider = input; return output; },
            prepareSound: (sound, file, format, completed) =>
            {
                if (sound.Id != fading.Id)
                {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) { throw new TimeoutException(); }
                }
                return SoundInstance.Create(sound, file, format, completed);
            });
        PlaybackEndReason? reason = null;
        engine.PlaybackEnded += (_, args) => { if (args.SoundId == fading.Id) { reason = args.EndReason; ended.Set(); } };
        engine.PlayAsync(fading, path).GetAwaiter().GetResult();
        ThreadPool.GetMinThreads(out var minWorker, out var minIo);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);
        Task? pending = null;
        try
        {
            // Queue four blocked preparations before rendering the fade. A
            // shared-pool implementation consumes every available worker.
            Assert.True(ThreadPool.SetMinThreads(1, minIo));
            Assert.True(ThreadPool.SetMaxThreads(4, maxIo));
            pending = Task.WhenAll(Enumerable.Range(0, 4).Select(_ => engine.PlayAsync(new SoundDefinition(), path)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            engine.Stop(fading.Id);
            provider!.Read(new float[4096], 0, 4096);
            Interlocked.Exchange(ref output.ConsumedSamples, 4096);
            Assert.True(ended.Wait(TimeSpan.FromSeconds(2)), "A blocked decoder starved fade completion");
            Assert.Equal(PlaybackEndReason.Stopped, reason);
        }
        finally
        {
            release.Set();
            ThreadPool.SetMaxThreads(maxWorker, maxIo);
            ThreadPool.SetMinThreads(minWorker, minIo);
            pending?.GetAwaiter().GetResult();
            engine.EmergencyStopAll();
        }
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
        public long ConsumedSamples;
        public long? PlayedSamples => Interlocked.Read(ref ConsumedSamples);
        public NAudio.Wave.PlaybackState PlaybackState { get; private set; }
        public void Play() => PlaybackState = NAudio.Wave.PlaybackState.Playing;
        public void Stop() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;
        public void Dispose() => Stop();
    }
}
