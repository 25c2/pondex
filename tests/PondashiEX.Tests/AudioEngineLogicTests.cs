using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Project;
using PondashiEX.Utility;

namespace PondashiEX.Tests;

public sealed class AudioEngineLogicTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailedLogging_RecordsPlaybackLifecycleOnlyWhenEnabled(bool enabled)
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("diagnostic.wav"));
        var writer = new StringWriter();
        await using var logger = new FileLogger(writer, 64) { DetailedLoggingEnabled = enabled };
        var sound = new SoundDefinition { Volume = 0.5F, Pitch = 1.25F };
        using (var engine = new AudioEngine(logger, (_, _) => new FakeAudioOutput()))
        {
            engine.SelectOutputDevice(2);
            await engine.PlayAsync(sound, path);
            engine.Pause(sound.Id);
            engine.Resume(sound.Id);
            engine.StopImmediately(sound.Id);
        }
        await logger.DisposeAsync();

        var output = writer.ToString();
        Assert.Contains("再生開始", output);
        if (enabled)
        {
            Assert.Contains($"play requested: sound={sound.Id}", output);
            Assert.Contains($"path={path}", output);
            Assert.Contains("prepare_ms=", output);
            Assert.Contains("device=2", output);
            Assert.Contains("pitch=1.25", output);
            Assert.Contains($"pause: sound={sound.Id}", output);
            Assert.Contains($"resume: sound={sound.Id}", output);
            Assert.Contains("reason=Stopped", output);
        }
        else { Assert.DoesNotContain("[DEBUG]", output); }
    }

    [Fact]
    public async Task RemovingBoostDuringPreparation_UsesLatestLimitAtPlaybackStart()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("pending-boost.wav");
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
        {
            for (var frame = 0; frame < 100; frame++) { writer.WriteSample(0.1F); }
        }
        using var release = new ManualResetEventSlim();
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ISampleProvider? mixed = null;
        using var engine = new AudioEngine(new NullLogger(), (_, provider) =>
        {
            mixed = provider;
            return new FakeAudioOutput();
        }, prepareSound: (definition, file, format, completed) =>
        {
            var instance = SoundInstance.Create(definition, file, format, completed);
            prepared.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) { instance.Dispose(); throw new TimeoutException(); }
            return instance;
        });
        var sound = new SoundDefinition { Volume = 2F, AllowVolumeBoost = true };
        var playing = engine.PlayAsync(sound, path);
        try
        {
            await prepared.Task.WaitAsync(TimeSpan.FromSeconds(5));
            sound.AllowVolumeBoost = false;
            sound.Volume = 1F;
            engine.ApplyLiveSettings(sound);
        }
        finally { release.Set(); }
        await playing;
        var samples = new float[2];
        mixed!.Read(samples, 0, samples.Length);
        Assert.All(samples, sample => Assert.Equal(0.1F, sample, 4));
    }

    [Fact]
    public async Task OptedInSourceBoost_AppliesLiveAndStopsWhenPermissionIsRemoved()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("quiet.wav");
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
        {
            for (var frame = 0; frame < 100; frame++) { writer.WriteSample(0.1F); }
        }
        ISampleProvider? mixed = null;
        using var engine = new AudioEngine(new NullLogger(), (_, provider) =>
        {
            mixed = provider;
            return new FakeAudioOutput();
        });
        var sound = new SoundDefinition { Volume = 1.5F, AllowVolumeBoost = true };
        engine.MasterVolume = 1F;
        await engine.PlayAsync(sound, path);
        var samples = new float[2];
        mixed!.Read(samples, 0, samples.Length);
        Assert.All(samples, sample => Assert.Equal(0.15F, sample, 4));
        sound.Volume = 2F;
        engine.ApplyLiveSettings(sound);
        engine.MasterVolume = 1F;
        mixed.Read(samples, 0, samples.Length);
        Assert.All(samples, sample => Assert.Equal(0.2F, sample, 4));
        engine.MasterMute = true;
        mixed.Read(samples, 0, samples.Length);
        Assert.All(samples, sample => Assert.Equal(0F, sample));
        Assert.Equal(1F, engine.MasterVolume);
        engine.MasterMute = false;
        mixed.Read(samples, 0, samples.Length);
        Assert.All(samples, sample => Assert.Equal(0.2F, sample, 4));
        engine.ApplyLiveSettings(new SoundDefinition { Id = sound.Id, Volume = 1F, AllowVolumeBoost = false });
        mixed.Read(samples, 0, samples.Length);
        Assert.All(samples, sample => Assert.Equal(0.1F, sample, 4));
        engine.FadeVolume(sound.Id, 2F, TimeSpan.Zero);
        mixed.Read(samples, 0, samples.Length);
        Assert.All(samples, sample => Assert.Equal(0.1F, sample, 4));
        engine.MasterVolume = 3F;
        Assert.Equal(1F, engine.MasterVolume);
    }

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
    public async Task StopAll_WithFadeEnabledFadesConfiguredSoundsAndImmediatelyStopsOthers()
    {
        using var directory = new TestDirectory();
        var firstPath = CreateWaveFile(directory.GetPath("fade.wav"));
        var secondPath = CreateWaveFile(directory.GetPath("immediate.wav"));
        using var engine = CreateEngine(out var output);
        var fading = new SoundDefinition
        {
            Id = Guid.NewGuid(),
            Name = "fade",
            FadeOut = new FadeSettings { Enabled = true, Value = 1.0f }
        };
        var immediate = new SoundDefinition { Id = Guid.NewGuid(), Name = "immediate" };

        await engine.PlayAsync(fading, firstPath);
        await engine.PlayAsync(immediate, secondPath);
        engine.FadeOutOnStopEnabled = false;
        engine.StopAll(useFadeOut: true);

        Assert.Equal(PondashiEX.Audio.PlaybackState.Fading, engine.GetState(fading.Id));
        Assert.Equal(PondashiEX.Audio.PlaybackState.Stopped, engine.GetState(immediate.Id));
        Assert.Contains(fading.Id, engine.GetActiveSoundIds());
        Assert.DoesNotContain(immediate.Id, engine.GetActiveSoundIds());
        Assert.Equal(NAudio.Wave.PlaybackState.Playing, output.PlaybackState);
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
    public async Task StopImmediately_BypassesFadeOutEvenWhenEnabled()
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("forced-stop.wav"));
        using var engine = CreateEngine(out _);
        var sound = new SoundDefinition
        {
            Id = Guid.NewGuid(),
            Name = "forced stop",
            FadeOut = new FadeSettings { Enabled = true, Value = 1.0f }
        };

        await engine.PlayAsync(sound, path);
        engine.FadeOutOnStopEnabled = true;
        engine.StopImmediately(sound.Id);

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
    public void SelectOutputDevice_WhenRequestedDeviceFails_DoesNotTryAnotherDevice()
    {
        var requested = new List<int>();
        using var engine = new AudioEngine(new NullLogger(), (number, _) =>
        {
            requested.Add(number);
            throw new InvalidOperationException("disconnected");
        });
        Assert.Throws<InvalidOperationException>(() => engine.SelectOutputDevice(7));
        Assert.Equal([7], requested);
        Assert.Equal(7, engine.OutputDeviceNumber);
    }

    [Fact]
    public async Task OutputStartFailure_DoesNotStartDefaultAndRequiresExplicitSelection()
    {
        var requested = new List<int>();
        using var engine = new AudioEngine(new NullLogger(), (number, _) =>
        {
            requested.Add(number);
            return new FailingAudioOutput();
        });
        var failure = new TaskCompletionSource<OutputDeviceFallbackEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.OutputDeviceFallback += (_, args) => failure.TrySetResult(args);
        engine.SelectOutputDevice(9);
        Assert.Throws<InvalidOperationException>(() => engine.SilentLoopEnabled = true);
        Assert.False((await failure.Task.WaitAsync(TimeSpan.FromSeconds(2))).Recovered);
        Assert.Equal([9], requested);
        Assert.Equal(9, engine.OutputDeviceNumber);
        Assert.Empty(engine.GetActiveSoundIds());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaybackDeviceFailure_StopsSoundsAndRequiresExplicitSelection(bool withoutException)
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("device-failure.wav"));
        var outputs = new List<FakeAudioOutput>();
        using var engine = new AudioEngine(new NullLogger(), (_, _) =>
        {
            var output = new FakeAudioOutput();
            outputs.Add(output);
            return output;
        });
        var failure = new TaskCompletionSource<OutputDeviceFallbackEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.OutputDeviceFallback += (_, args) => failure.TrySetResult(args);
        engine.SelectOutputDevice(3);
        var sound = new SoundDefinition();
        await engine.PlayAsync(sound, path);
        outputs[0].RaisePlaybackFailure(withoutException ? null : new InvalidOperationException("device unplugged"));
        Assert.False((await failure.Task.WaitAsync(TimeSpan.FromSeconds(2))).Recovered);
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.Equal(3, engine.OutputDeviceNumber);
        await engine.PlayAsync(sound, path);
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.Single(outputs);
        engine.SelectOutputDevice(3);
        await engine.PlayAsync(sound, path);
        Assert.Equal(2, outputs.Count);
        Assert.Contains(sound.Id, engine.GetActiveSoundIds());
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

    [Fact]
    public async Task FailedRetrigger_OriginalSoundStillCompletesNaturally()
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("original.wav"));
        ISampleProvider? mixer = null;
        using var engine = new AudioEngine(new NullLogger(), (_, provider) =>
        {
            mixer = provider;
            return new FakeAudioOutput();
        });
        var ended = new TaskCompletionSource<PlaybackEndReason?>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackEnded += (_, args) => ended.TrySetResult(args.EndReason);
        var sound = new SoundDefinition();
        await engine.PlayAsync(sound, path);
        await engine.PlayAsync(sound, directory.GetPath("missing.wav"));
        var buffer = new float[16_384];
        mixer!.Read(buffer, 0, buffer.Length);

        Assert.Equal(PlaybackEndReason.Natural, await ended.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.Equal(PondashiEX.Audio.PlaybackState.Stopped, engine.GetState(sound.Id));
    }

    private static string CreateWaveFile(string path)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(44_100, 16, 1));
        writer.Write(new byte[8_820], 0, 8_820);
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmergencyStopAll_StopsPlayingPausedFadingAndOutput(bool silentLoop)
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("emergency.wav"));
        using var engine = CreateEngine(out var output);
        engine.SilentLoopEnabled = silentLoop;
        var sounds = Enumerable.Range(0, 3).Select(_ => new SoundDefinition
        {
            Loop = new LoopSettings { Enabled = true },
            FadeOut = new FadeSettings { Enabled = true, Value = 10 }
        }).ToArray();
        foreach (var sound in sounds) { await engine.PlayAsync(sound, path); }
        engine.Pause(sounds[1].Id);
        engine.Stop(sounds[2].Id);
        Assert.Equal(PondashiEX.Audio.PlaybackState.Paused, engine.GetState(sounds[1].Id));
        Assert.Equal(PondashiEX.Audio.PlaybackState.Fading, engine.GetState(sounds[2].Id));

        engine.EmergencyStopAll();

        Assert.Empty(engine.GetActiveSoundIds());
        Assert.All(sounds, sound => Assert.Equal(PondashiEX.Audio.PlaybackState.Stopped, engine.GetState(sound.Id)));
        Assert.Equal(NAudio.Wave.PlaybackState.Stopped, output.PlaybackState);
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData("emergency")]
    [InlineData("exclusive-peer-stop")]
    [InlineData("device-change")]
    public async Task StopOrDeviceChange_InvalidatesInFlightPreparationAndAllowsNewExplicitPlay(string operation)
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("pending.wav"));
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var requests = 0;
        var logWriter = new StringWriter();
        await using var logger = new FileLogger(logWriter, 64) { DetailedLoggingEnabled = true };
        using var engine = new AudioEngine(logger, (_, _) => new FakeAudioOutput(),
            prepareSound: (sound, file, format, completed) =>
            {
                var instance = SoundInstance.Create(sound, file, format, completed);
                if (Interlocked.Increment(ref requests) == 1)
                {
                    prepared.SetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) { instance.Dispose(); throw new TimeoutException(); }
                }
                return instance;
            });
        var sound = new SoundDefinition();
        var starts = 0;
        engine.PlaybackStarted += (_, _) => starts++;
        var pending = engine.PlayAsync(sound, path);
        try
        {
            await prepared.Task.WaitAsync(TimeSpan.FromSeconds(5));
            switch (operation)
            {
                case "emergency": engine.EmergencyStopAll(); break;
                case "exclusive-peer-stop": engine.StopImmediately(sound.Id); break;
                case "device-change": engine.SelectOutputDevice(4); break;
            }
        }
        finally { release.Set(); }
        await pending;
        Assert.Empty(engine.GetActiveSoundIds());
        Assert.Equal(0, starts);
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        await engine.PlayAsync(sound, path);
        Assert.Equal(1, starts);
        Assert.Equal(PondashiEX.Audio.PlaybackState.Playing, engine.GetState(sound.Id));
        await logger.DisposeAsync();
        Assert.Contains($"play discarded: sound={sound.Id}", logWriter.ToString());
        Assert.Contains("reason=superseded", logWriter.ToString());
    }

    [Fact]
    public async Task PlaybackStartTiming_IsOptInAndDistinguishesFirstAndRepeatedPreparation()
    {
        using var directory = new TestDirectory();
        var path = CreateWaveFile(directory.GetPath("timing.wav"));
        using var engine = CreateEngine(out _);
        var sound = new SoundDefinition();
        await engine.PlayAsync(sound, path);
        Assert.Empty(engine.GetPlaybackStartTimings());
        engine.PlaybackTimingEnabled = true;
        await engine.PlayAsync(sound, path);
        await engine.PlayAsync(sound, path);
        var timings = engine.GetPlaybackStartTimings();
        Assert.Equal([1, 2], timings.Select(timing => timing.Attempt));
        Assert.All(timings, timing =>
        {
            Assert.Equal(sound.Id, timing.SoundId);
            Assert.True(timing.PreparationMilliseconds >= 0);
            Assert.True(timing.StartMilliseconds >= timing.PreparationMilliseconds);
        });
        engine.PlaybackTimingEnabled = false;
        await engine.PlayAsync(sound, path);
        Assert.Equal(2, engine.GetPlaybackStartTimings().Count);
    }

    private sealed class FakeAudioOutput : IAudioOutput
    {
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public NAudio.Wave.PlaybackState PlaybackState { get; private set; }

        public void Play() => PlaybackState = NAudio.Wave.PlaybackState.Playing;

        public void Stop() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;

        public void Dispose() => PlaybackState = NAudio.Wave.PlaybackState.Stopped;

        public void RaisePlaybackFailure(Exception? exception)
        {
            PlaybackState = NAudio.Wave.PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs(exception));
        }
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
