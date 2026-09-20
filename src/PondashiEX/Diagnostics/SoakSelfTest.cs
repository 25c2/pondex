using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using NAudio.Wave;
using PondashiEX.Audio;
using PondashiEX.Utility;

namespace PondashiEX.Diagnostics;

internal static class SoakSelfTest
{
    public static bool TryParseDuration(string[] args, out TimeSpan duration)
    {
        const string prefix = "--soak-test-minutes=";
        var argument = args.FirstOrDefault(
            value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (argument is null)
        {
            duration = default;
            return false;
        }

        var valueText = argument[prefix.Length..];
        if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes)
            || minutes is < 0.01 or > 1_440)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "--soak-test-minutesは0.01〜1440の範囲で指定してください。");
        }

        duration = TimeSpan.FromMinutes(minutes);
        return true;
    }

    public static async Task<SoakSelfTestResult> RunAsync(
        TimeSpan duration,
        IAppLogger logger,
        CancellationToken cancellationToken = default)
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "PondashiEX.SoakTest",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var mp3Path = Path.Combine(tempDirectory, "soak.mp3");
        var oggPath = Path.Combine(tempDirectory, "soak.ogg");
        var opusPath = Path.Combine(tempDirectory, "soak-opus.ogg");
        File.WriteAllBytes(mp3Path, Convert.FromBase64String(AudioSelfTestFixtures.Mp3Base64));
        File.WriteAllBytes(oggPath, Convert.FromBase64String(AudioSelfTestFixtures.OggBase64));
        File.WriteAllBytes(opusPath, Convert.FromBase64String(AudioSelfTestFixtures.OpusBase64));

        var process = Process.GetCurrentProcess();
        var startHandles = 0;
        var maximumPrivateBytes = 0L;
        var iterations = 0L;
        var failures = new ConcurrentQueue<string>();
        var stopwatch = new Stopwatch();
        var outputSamples = 0L;

        try
        {
            using (var engine = new AudioEngine(logger))
            {
                engine.PlaybackFailed += (_, eventArgs) =>
                    failures.Enqueue($"{eventArgs.Message} {eventArgs.Exception.Message}");
                engine.SelectOutputDevice(-1);
                engine.SilentLoopEnabled = true;
                DecodeEntireFile(mp3Path);
                DecodeEntireFile(oggPath);
                DecodeEntireFile(opusPath);
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                process.Refresh();
                startHandles = process.HandleCount;
                maximumPrivateBytes = process.PrivateMemorySize64;
                var initialSamples = engine.OutputSamplesRead;
                var lastSamples = initialSamples;
                var lastProgress = TimeSpan.Zero;
                stopwatch.Start();

                while (stopwatch.Elapsed < duration)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DecodeEntireFile(mp3Path);
                    DecodeEntireFile(oggPath);
                    DecodeEntireFile(opusPath);
                    iterations++;

                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                    var currentSamples = engine.OutputSamplesRead;
                    if (currentSamples > lastSamples)
                    {
                        lastSamples = currentSamples;
                        lastProgress = stopwatch.Elapsed;
                    }
                    else if (stopwatch.Elapsed - lastProgress > TimeSpan.FromSeconds(3))
                    {
                        failures.Enqueue("音声出力のsample進行が3秒以上停止しました。");
                        break;
                    }

                    process.Refresh();
                    maximumPrivateBytes = Math.Max(maximumPrivateBytes, process.PrivateMemorySize64);
                }

                outputSamples = engine.OutputSamplesRead - initialSamples;
                engine.SilentLoopEnabled = false;
            }
        }
        catch (Exception exception)
        {
            failures.Enqueue(exception.ToString());
        }
        finally
        {
            stopwatch.Stop();
            TryDeleteDirectory(tempDirectory, logger);
        }

        process.Refresh();
        maximumPrivateBytes = Math.Max(maximumPrivateBytes, process.PrivateMemorySize64);
        return new SoakSelfTestResult(
            DateTimeOffset.Now,
            duration,
            stopwatch.Elapsed,
            iterations,
            outputSamples,
            startHandles,
            process.HandleCount,
            maximumPrivateBytes,
            failures.ToArray());
    }

    public static string SaveReport(SoakSelfTestResult result, string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var reportPath = Path.Combine(logDirectory, "soak-self-test.txt");
        File.WriteAllText(
            reportPath,
            result.ToReport(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return reportPath;
    }

    private static void DecodeEntireFile(string path)
    {
        using var decoder = AudioDecoderFactory.Open(path);
        var provider = decoder.ToSampleProvider();
        var buffer = new float[4_096];
        while (provider.Read(buffer, 0, buffer.Length) > 0)
        {
        }
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
            logger.Warning($"soak診断の一時ファイルを削除できませんでした: {exception.Message}");
        }
    }
}

internal sealed record SoakSelfTestResult(
    DateTimeOffset Timestamp,
    TimeSpan RequestedDuration,
    TimeSpan ActualDuration,
    long Iterations,
    long OutputSamples,
    int StartHandleCount,
    int EndHandleCount,
    long MaximumPrivateBytes,
    IReadOnlyList<string> Failures)
{
    public bool Success => Failures.Count == 0 && Iterations > 0 && OutputSamples > 0;

    public string ToReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("PondashiEX soak self-test");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Timestamp: {Timestamp:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Requested duration: {RequestedDuration}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Actual duration: {ActualDuration}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Result: {(Success ? "PASS" : "FAIL")}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Decode iterations: {Iterations}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Output samples: {OutputSamples}");
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Handle count: {StartHandleCount} -> {EndHandleCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Maximum private bytes: {MaximumPrivateBytes}");
        foreach (var failure in Failures)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"[FAIL] {failure}");
        }

        return builder.ToString();
    }
}
