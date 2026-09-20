using System.Diagnostics;
using System.Globalization;
using System.Text;
using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Input;
using PondashiEX.Project;
using PondashiEX.Utility;

namespace PondashiEX.Diagnostics;

internal static class AudioSelfTest
{
    public static async Task<AudioSelfTestResult> RunAsync(
        IAppLogger logger,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AudioSelfTestItem>();
        var stopwatch = Stopwatch.StartNew();
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "PondashiEX.SelfTest",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var wavPath = Path.Combine(tempDirectory, "self-test.wav");
            WriteSineWave(wavPath);
            var mp3Path = WriteFixture(tempDirectory, "self-test.mp3", AudioSelfTestFixtures.Mp3Base64);
            var oggPath = WriteFixture(tempDirectory, "self-test.ogg", AudioSelfTestFixtures.OggBase64);
            var opusPath = WriteFixture(tempDirectory, "self-test-opus.ogg", AudioSelfTestFixtures.OpusBase64);

            foreach (var (name, path) in new[]
            {
                ("WAV actual output", wavPath),
                ("MP3 managed decode + actual output", mp3Path),
                ("Ogg Vorbis managed decode + actual output", oggPath),
                ("Ogg Opus managed decode + actual output", opusPath)
            })
            {
                results.Add(await TestPlaybackAsync(name, path, logger, cancellationToken));
            }

            results.Add(await TestPitchAndLoopAsync(wavPath, logger, cancellationToken));
            results.Add(await TestSilentLoopAsync(logger, cancellationToken));

            results.Add(TestKeyboardHook());
            results.Add(new AudioSelfTestItem(
                "Per-Monitor V2 DPI mode",
                Application.HighDpiMode == HighDpiMode.PerMonitorV2,
                $"HighDpiMode={Application.HighDpiMode}"));
        }
        finally
        {
            TryDeleteDirectory(tempDirectory, logger);
        }

        stopwatch.Stop();
        return new AudioSelfTestResult(
            DateTimeOffset.Now,
            Environment.OSVersion.VersionString,
            Environment.Is64BitProcess,
            stopwatch.Elapsed,
            results);
    }

    private static async Task<AudioSelfTestItem> TestPitchAndLoopAsync(
        string path,
        IAppLogger logger,
        CancellationToken cancellationToken)
    {
        const string name = "Pitch + loop start sample actual output";
        using var engine = new AudioEngine(logger);
        var sound = new SoundDefinition
        {
            Id = Guid.NewGuid(),
            Name = name,
            File = path,
            Pitch = 1.5f,
            Loop = new LoopSettings { Enabled = true, StartSample = 1_000 }
        };
        var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackFailed += (_, eventArgs) => failure.TrySetResult(eventArgs.Exception.Message);

        try
        {
            engine.SelectOutputDevice(-1);
            await engine.PlayAsync(sound, path, cancellationToken);
            var delay = Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
            var completed = await Task.WhenAny(delay, failure.Task);
            if (completed == failure.Task)
            {
                return new AudioSelfTestItem(name, false, await failure.Task);
            }

            var soundSamples = engine.GetSoundSamplesRead(sound.Id);
            var success = engine.GetState(sound.Id) == PondashiEX.Audio.PlaybackState.Playing
                && soundSamples > 29_400;
            return new AudioSelfTestItem(
                name,
                success,
                $"state={engine.GetState(sound.Id)}, soundSamplesRead={soundSamples}");
        }
        catch (Exception exception)
        {
            return new AudioSelfTestItem(name, false, exception.Message);
        }
        finally
        {
            engine.StopAll();
        }
    }

    private static async Task<AudioSelfTestItem> TestSilentLoopAsync(
        IAppLogger logger,
        CancellationToken cancellationToken)
    {
        const string name = "Silent loop continuous output";
        using var engine = new AudioEngine(logger);
        var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackFailed += (_, eventArgs) => failure.TrySetResult(eventArgs.Exception.Message);

        try
        {
            engine.SelectOutputDevice(-1);
            engine.SilentLoopEnabled = true;
            var before = engine.OutputSamplesRead;
            var delay = Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            var completed = await Task.WhenAny(delay, failure.Task);
            if (completed == failure.Task)
            {
                return new AudioSelfTestItem(name, false, await failure.Task);
            }

            var samples = engine.OutputSamplesRead - before;
            return new AudioSelfTestItem(
                name,
                samples > 0 && engine.GetActiveSoundIds().Count == 0,
                $"silentSamplesRead={samples}");
        }
        catch (Exception exception)
        {
            return new AudioSelfTestItem(name, false, exception.Message);
        }
        finally
        {
            engine.SilentLoopEnabled = false;
        }
    }

    private static async Task<AudioSelfTestItem> TestPlaybackAsync(
        string name,
        string path,
        IAppLogger logger,
        CancellationToken cancellationToken)
    {
        using var engine = new AudioEngine(logger);
        var sound = new SoundDefinition
        {
            Id = Guid.NewGuid(),
            Name = name,
            File = path
        };
        var completion = new TaskCompletionSource<AudioSelfTestItem>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackEnded += (_, eventArgs) =>
        {
            if (eventArgs.SoundId == sound.Id && eventArgs.EndReason == PlaybackEndReason.Natural)
            {
                completion.TrySetResult(new AudioSelfTestItem(name, true, "natural completion"));
            }
        };
        engine.PlaybackFailed += (_, eventArgs) =>
        {
            if (eventArgs.SoundId == sound.Id || eventArgs.SoundId == Guid.Empty)
            {
                completion.TrySetResult(new AudioSelfTestItem(
                    name,
                    false,
                    $"{eventArgs.Message} {eventArgs.Exception.Message}"));
            }
        };

        try
        {
            engine.SelectOutputDevice(-1);
            await engine.PlayAsync(sound, path, cancellationToken);
            var completed = await Task.WhenAny(
                completion.Task,
                Task.Delay(TimeSpan.FromSeconds(4), cancellationToken));
            if (completed != completion.Task)
            {
                return new AudioSelfTestItem(
                    name,
                    false,
                    $"4秒以内に再生完了しませんでした。outputSamplesRead={engine.OutputSamplesRead}, soundSamplesRead={engine.GetSoundSamplesRead(sound.Id)}, state={engine.GetState(sound.Id)}");
            }

            return await completion.Task;
        }
        catch (Exception exception)
        {
            return new AudioSelfTestItem(name, false, exception.Message);
        }
        finally
        {
            engine.StopAll();
        }
    }

    public static string SaveReport(AudioSelfTestResult result, string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var reportPath = Path.Combine(logDirectory, "audio-self-test.txt");
        File.WriteAllText(
            reportPath,
            result.ToReport(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return reportPath;
    }

    private static AudioSelfTestItem TestKeyboardHook()
    {
        try
        {
            using var hook = new GlobalKeyboardHook();
            hook.Enable();
            var enabled = hook.IsEnabled;
            hook.Disable();
            return new AudioSelfTestItem(
                "Background Input keyboard hook",
                enabled && !hook.IsEnabled,
                enabled ? "enable/disable succeeded" : "hook did not become active");
        }
        catch (Exception exception)
        {
            return new AudioSelfTestItem("Background Input keyboard hook", false, exception.Message);
        }
    }

    private static void WriteSineWave(string path)
    {
        const int sampleRate = 44_100;
        const int sampleCount = sampleRate / 3;
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1));
        for (var index = 0; index < sampleCount; index++)
        {
            var sample = (short)(Math.Sin(2 * Math.PI * 440 * index / sampleRate) * short.MaxValue * 0.15);
            var bytes = BitConverter.GetBytes(sample);
            writer.Write(bytes, 0, bytes.Length);
        }
    }

    private static string WriteFixture(string directory, string name, string base64)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, Convert.FromBase64String(base64));
        return path;
    }

    private static void TryDeleteDirectory(string path, IAppLogger logger)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception)
        {
            logger.Warning($"動作診断の一時ファイルを削除できませんでした: {exception.Message}");
        }
    }
}

internal sealed record AudioSelfTestItem(string Name, bool Success, string Detail);

internal sealed record AudioSelfTestResult(
    DateTimeOffset Timestamp,
    string OperatingSystem,
    bool Is64BitProcess,
    TimeSpan Duration,
    IReadOnlyList<AudioSelfTestItem> Items)
{
    public bool Success => Items.All(item => item.Success);

    public string ToReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("PondashiEX audio self-test");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Timestamp: {Timestamp:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"OS: {OperatingSystem}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"64-bit process: {Is64BitProcess}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Duration: {Duration}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Result: {(Success ? "PASS" : "FAIL")}");
        foreach (var item in Items)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"[{(item.Success ? "PASS" : "FAIL")}] {item.Name}: {item.Detail}");
        }

        return builder.ToString();
    }
}
