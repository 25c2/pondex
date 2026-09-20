using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Project;
using PondashiEX.Utility;

namespace PondashiEX.Tests;

public sealed class AudioEngineLogicTests
{
    [Fact]
    public async Task StopAll_StopsEveryActiveSoundWithoutRealAudioDevice()
    {
        using var directory = new TestDirectory();
        var firstPath = CreateWaveFile(directory.GetPath("first.wav"));
        var secondPath = CreateWaveFile(directory.GetPath("second.wav"));
        using var engine = CreateEngine(out var output);
        var first = new SoundDefinition { Id = Guid.NewGuid(), Name = "first" };
        var second = new SoundDefinition { Id = Guid.NewGuid(), Name = "second" };

        await engine.PlayAsync(first, firstPath);
        await engine.PlayAsync(second, secondPath);
        engine.StopAll();

        Assert.Empty(engine.GetActiveSoundIds());
        Assert.Equal(PondashiEX.Audio.PlaybackState.Stopped, engine.GetState(first.Id));
        Assert.Equal(PondashiEX.Audio.PlaybackState.Stopped, engine.GetState(second.Id));
        Assert.Equal(NAudio.Wave.PlaybackState.Stopped, output.PlaybackState);
    }

    [Fact]
    public async Task Stop_UsesConfiguredFadeOutWhenEnabled()
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("fade-stop.wav"));
        using var engine = CreateEngine(out _);
        var sound = new SoundDefinition
        {
            Id = Guid.NewGuid(),
            Name = "fade stop",
            FadeOut = new FadeSettings { Enabled = true, Value = 1.0f }
        };

        await engine.PlayAsync(sound, path);
        engine.FadeOutOnStopEnabled = true;
        engine.Stop(sound.Id);

        Assert.Equal(PondashiEX.Audio.PlaybackState.Fading, engine.GetState(sound.Id));
        Assert.Contains(sound.Id, engine.GetActiveSoundIds());
    }

    [Fact]
    public async Task Stop_BypassesConfiguredFadeOutWhenDisabled()
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("immediate-stop.wav"));
        using var engine = CreateEngine(out _);
        var sound = new SoundDefinition
        {
            Id = Guid.NewGuid(),
            Name = "immediate stop",
            FadeOut = new FadeSettings { Enabled = true, Value = 1.0f }
        };

        await engine.PlayAsync(sound, path);
        engine.FadeOutOnStopEnabled = false;
        engine.Stop(sound.Id);

        Assert.Equal(PondashiEX.Audio.PlaybackState.Stopped, engine.GetState(sound.Id));
        Assert.DoesNotContain(sound.Id, engine.GetActiveSoundIds());
    }

    [Fact]
    public async Task InvalidSound_DoesNotDestroyAlreadyPlayingSound()
    {
        using var directory = new TestDirectory();
        var goodPath = CreateWaveFile(directory.GetPath("good.wav"));
        using var engine = CreateEngine(out _);
        var good = new SoundDefinition { Id = Guid.NewGuid(), Name = "good" };
        var missing = new SoundDefinition { Id = Guid.NewGuid(), Name = "missing" };
        SoundErrorEventArgs? reported = null;
        engine.PlaybackFailed += (_, eventArgs) => reported = eventArgs;

        await engine.PlayAsync(good, goodPath);
        await engine.PlayAsync(missing, directory.GetPath("missing.wav"));

        Assert.Contains(good.Id, engine.GetActiveSoundIds());
        Assert.DoesNotContain(missing.Id, engine.GetActiveSoundIds());
        Assert.NotNull(reported);
        Assert.Equal(missing.Id, reported.SoundId);
    }

    [Fact]
    public async Task SameGroup_AllowsSimultaneousPlayback()
    {
        using var directory = new TestDirectory();
        var firstPath = CreateWaveFile(directory.GetPath("first.wav"));
        var secondPath = CreateWaveFile(directory.GetPath("second.wav"));
        using var engine = CreateEngine(out _);
        var first = new SoundDefinition { Id = Guid.NewGuid(), Name = "first", Group = "BGM" };
        var second = new SoundDefinition { Id = Guid.NewGuid(), Name = "second", Group = "BGM" };

        await engine.PlayAsync(first, firstPath);
        await engine.PlayAsync(second, secondPath);

        Assert.Contains(first.Id, engine.GetActiveSoundIds());
        Assert.Contains(second.Id, engine.GetActiveSoundIds());
    }

    [Fact]
    public async Task PauseAndResume_ChangePlaybackStateWithoutRealAudioDevice()
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("pause.wav"));
        using var engine = CreateEngine(out _);
        var sound = new SoundDefinition { Id = Guid.NewGuid(), Name = "pause" };

        await engine.PlayAsync(sound, path);
        engine.Pause(sound.Id);

        Assert.Equal(PondashiEX.Audio.PlaybackState.Paused, engine.GetState(sound.Id));

        engine.Resume(sound.Id);

        Assert.Equal(PondashiEX.Audio.PlaybackState.Playing, engine.GetState(sound.Id));
    }

    [Fact]
    public void SelectOutputDevice_WhenRequestedDeviceFails_FallsBackToDefault()
    {
        var requestedDevices = new List<int>();
        OutputDeviceFallbackEventArgs? fallback = null;
        using var engine = new AudioEngine(
            new NullLogger(),
            (deviceNumber, _) =>
            {
                requestedDevices.Add(deviceNumber);
                return deviceNumber == 7
                    ? throw new InvalidOperationException("selected device disappeared")
                    : new FakeAudioOutput();
            });
        engine.OutputDeviceFallback += (_, eventArgs) => fallback = eventArgs;

        engine.SelectOutputDevice(7);

        Assert.Equal([7, -1], requestedDevices);
        Assert.Equal(-1, engine.OutputDeviceNumber);
        Assert.NotNull(fallback);
        Assert.True(fallback.Recovered);
        Assert.Equal(7, fallback.FailedDeviceNumber);
    }

    [Fact]
    public void OutputStartFailure_OnSelectedDevice_FallsBackAndStartsDefault()
    {
        var defaultOutput = new FakeAudioOutput();
        OutputDeviceFallbackEventArgs? fallback = null;
        using var engine = new AudioEngine(
            new NullLogger(),
            (deviceNumber, _) => deviceNumber == 9 ? new FailingAudioOutput() : defaultOutput);
        engine.OutputDeviceFallback += (_, eventArgs) => fallback = eventArgs;
        engine.SelectOutputDevice(9);

        engine.SilentLoopEnabled = true;

        Assert.Equal(-1, engine.OutputDeviceNumber);
        Assert.Equal(NAudio.Wave.PlaybackState.Playing, defaultOutput.PlaybackState);
        Assert.NotNull(fallback);
        Assert.True(fallback.Recovered);
        Assert.Equal(9, fallback.FailedDeviceNumber);
    }

    [Fact]
    public async Task PlaybackDeviceFailure_StopsSoundsAndReinitializesDefaultOutput()
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("device-failure.wav"));
        var outputs = new List<FakeAudioOutput>();
        using var engine = new AudioEngine(
            new NullLogger(),
            (_, _) =>
            {
                var output = new FakeAudioOutput();
                outputs.Add(output);
                return output;
            });
        var recovered = new TaskCompletionSource<OutputDeviceFallbackEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        engine.OutputDeviceFallback += (_, eventArgs) => recovered.TrySetResult(eventArgs);
        engine.SelectOutputDevice(3);
        var sound = new SoundDefinition { Id = Guid.NewGuid(), Name = "device failure" };
        await engine.PlayAsync(sound, path);

        outputs[0].RaisePlaybackFailure(new InvalidOperationException("device unplugged"));
        var result = await recovered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.Recovered);
        Assert.Equal(3, result.FailedDeviceNumber);
        Assert.Equal(-1, engine.OutputDeviceNumber);
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.Equal(2, outputs.Count);
    }

    [Fact]
    public async Task RepeatedPlayStop_ReleasesEveryDecoderAndFileHandle()
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("soak.wav"));
        using var engine = CreateEngine(out _);
        var sound = new SoundDefinition { Id = Guid.NewGuid(), Name = "soak" };

        for (var iteration = 0; iteration < 250; iteration++)
        {
            await engine.PlayAsync(sound, path);
            engine.Stop(sound.Id);
        }

        Assert.Empty(engine.GetActiveSoundIds());
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task OutputStartFailure_DoesNotLeaveDisposedInstanceRegistered()
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("valid.wav"));
        using var engine = new AudioEngine(new NullLogger(), (_, _) => new FailingAudioOutput());
        var sound = new SoundDefinition { Id = Guid.NewGuid(), Name = "output failure" };
        SoundErrorEventArgs? reported = null;
        engine.PlaybackFailed += (_, eventArgs) => reported = eventArgs;

        await engine.PlayAsync(sound, path);

        Assert.Empty(engine.GetActiveSoundIds());
        Assert.Equal(PondashiEX.Audio.PlaybackState.Stopped, engine.GetState(sound.Id));
        Assert.NotNull(reported);
    }

    private static AudioEngine CreateEngine(out FakeAudioOutput output)
    {
        output = new FakeAudioOutput();
        var captured = output;
        return new AudioEngine(new NullLogger(), (_, _) => captured);
    }

    private static string CreateWaveFile(string path)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(44_100, 16, 1));
        writer.Write(new byte[8_820], 0, 8_820);
        return path;
    }

    private sealed class FakeAudioOutput : IAudioOutput
    {
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public NAudio.Wave.PlaybackState PlaybackState { get; private set; }

        public void Play() => PlaybackState = NAudio.Wave.PlaybackState.Playing;

        public void Stop() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;

        public void Dispose() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;

        public void RaisePlaybackFailure(Exception exception) =>
            PlaybackStopped?.Invoke(this, new StoppedEventArgs(exception));
    }

    private sealed class FailingAudioOutput : IAudioOutput
    {
        public event EventHandler<StoppedEventArgs>? PlaybackStopped
        {
            add { }
            remove { }
        }

        public NAudio.Wave.PlaybackState PlaybackState => NAudio.Wave.PlaybackState.Stopped;

        public void Play() => throw new InvalidOperationException("simulated output failure");

        public void Stop() { }

        public void Dispose() { }
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }

        public void Warning(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }
}
